using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>使用命名的 <see cref="HttpClient"/> 将 HTTP 请求和操作作为持久化任务的步骤执行。</summary>
/// <remarks>
/// 命名客户端的处理程序链以 <see cref="DurableIdempotencyKeyHandler"/> 开头，它为每个步骤中使用非安全方法的
/// 请求设置该步骤的 <c>Idempotency-Key</c>。
/// </remarks>
internal sealed class DurableHttpClient(IHttpClientFactory clientFactory, string name, DurableHttpClientOptions options) : IDurableHttpClient
{
    /// <summary>事件流未指定重连间隔时使用的间隔，与浏览器的 <c>EventSource</c> 一致。</summary>
    private static readonly TimeSpan DefaultReconnectionInterval = TimeSpan.FromSeconds(3);

    private readonly DurableHttpClientOptions _options = options;
    private readonly JsonSerializerOptions _json = options.SerializerOptions;

    public DurableTask<DurableHttpResponse> SendAsync(Func<HttpRequestMessage> createRequest)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        return DurableTask.Run(
            static async (state, cancellationToken) =>
            {
                using var request = state.createRequest();
                // 客户端缓冲响应内容，最大不超过 DurableHttpClientOptions.MaxResponseContentLength。
                using var response = await state.client.CreateClient().SendAsync(request, cancellationToken);
                return await DurableHttpResponse.ReadAsync(response, cancellationToken);
            },
            (client: this, createRequest));
    }

    public DurableTask<DurableHttpOperation> StartOperationAsync(Func<HttpRequestMessage> createRequest)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        return StartAsync(this, createRequest);

        static async DurableTask<DurableHttpOperation> StartAsync(DurableHttpClient client, Func<HttpRequestMessage> createRequest)
        {
            var operation = DurableHttpOperation.FromStart(await client.SendAsync(createRequest));
            if (operation is { Location: { } location, Response.RequestUri: { } requestUri } && !client._options.FollowOperationLocation(requestUri, location))
            {
                throw new HttpRequestException(
                    $"'{requestUri}' located its operation at '{location}', which DurableHttpClientOptions.FollowOperationLocation doesn't follow.",
                    inner: null,
                    operation.Response.StatusCode);
            }

            return operation;
        }
    }

    public DurableTask<TResult> WaitForOperationAsync<TResult>(DurableHttpOperation operation, DurableHttpOperationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return WaitAsync<TResult>(operation, options ?? DurableHttpOperationOptions.Default);
    }

    public DurableTask<DurableOperation> GetOperationAsync(Uri operationLocation)
    {
        ArgumentNullException.ThrowIfNull(operationLocation);
        return ReadOperationAsync(this, () => new HttpRequestMessage(HttpMethod.Get, operationLocation));
    }

    public DurableTask<DurableOperation> CancelOperationAsync(Uri operationLocation, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(operationLocation);
        var cancelUri = Append(operationLocation, ":cancel");
        return ReadOperationAsync(this, () => new HttpRequestMessage(HttpMethod.Post, cancelUri)
        {
            Content = JsonContent.Create(new DurableOperationCancelRequest(reason), DurableHttpJsonContext.Default.DurableOperationCancelRequest),
        });
    }

    public DurableTask SignalOperationAsync<T>(Uri operationLocation, string signalName, T value)
    {
        ArgumentNullException.ThrowIfNull(operationLocation);
        ArgumentException.ThrowIfNullOrEmpty(signalName);
        var signalUri = Append(operationLocation, $"/signals/{Uri.EscapeDataString(signalName)}");
        return SignalAsync(this, () => new HttpRequestMessage(HttpMethod.Post, signalUri) { Content = JsonContent.Create(value, _json.GetTypeInfo<T>()) });

        static async DurableTask SignalAsync(DurableHttpClient client, Func<HttpRequestMessage> createRequest) =>
            (await client.SendAsync(createRequest)).EnsureSuccessStatusCode();
    }

    public async IAsyncEnumerable<DurableOperation> WatchOperationAsync(Uri operationLocation, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationLocation);
        var eventsUri = Append(operationLocation, "/events");
        var client = CreateClient();
        string? lastEventId = null;
        var reconnectionInterval = DefaultReconnectionInterval;
        while (true)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, eventsUri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (lastEventId is not null)
                {
                    request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
                }

                HttpResponseMessage? response;
                try
                {
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (HttpRequestException exception) when (exception.StatusCode is null && !cancellationToken.IsCancellationRequested)
                {
                    // 服务器不可达，因此像 EventSource 一样等待该间隔后重新连接。
                    response = null;
                }

                if (response is not null)
                {
                    using (response)
                    {
                        // 在终态之后进行重连时，服务器返回 204 No Content。
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            yield break;
                        }

                        response.EnsureSuccessStatusCode();
                        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        var parser = SseParser.Create(stream, static (eventType, data) => eventType == "status" ? DurableOperation.Read(data) : null);
                        await using var events = parser.EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
                        while (await MoveNextAsync(events, cancellationToken))
                        {
                            if (events.Current.Data is not { } operation)
                            {
                                continue;
                            }

                            yield return operation;
                            if (operation.IsTerminal)
                            {
                                yield break;
                            }
                        }

                        // 在操作进入终态前流已结束（例如代理关闭连接），因此
                        // 携带最后一个事件的标识符，等待服务器声明的间隔后重新连接。
                        lastEventId = parser.LastEventId is { Length: > 0 } id ? id : lastEventId;
                        reconnectionInterval = parser.ReconnectionInterval is var interval && interval > TimeSpan.Zero && interval != Timeout.InfiniteTimeSpan
                            ? interval
                            : reconnectionInterval;
                    }
                }
            }

            await Task.Delay(reconnectionInterval, cancellationToken);
        }

        // 读取下一个事件；流结束时返回 false，包括连接在流传输途中断开的情况。
        static async ValueTask<bool> MoveNextAsync(IAsyncEnumerator<SseItem<DurableOperation?>> events, CancellationToken cancellationToken)
        {
            try
            {
                return await events.MoveNextAsync();
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    private HttpClient CreateClient()
    {
        var client = clientFactory.CreateClient(name);
        client.MaxResponseContentBufferSize = _options.MaxResponseContentLength;
        return client;
    }

    private async DurableTask<TResult> WaitAsync<TResult>(DurableHttpOperation operation, DurableHttpOperationOptions options)
    {
        if (operation.IsCompleted)
        {
            return TryComplete(operation.Response, isStatusMonitorResponse: false, out TResult completed)
                ? completed
                : throw new UnreachableException("A completed start response carries its result or its failure.");
        }

        // 正在运行的操作总是具有一个位置，由 FromStart 确定。
        var location = operation.Location!;
        if (options.Mode == DurableHttpWaitMode.Events && operation.IsStatusMonitor)
        {
            var terminal = await DurableTask.Run(
                static async (state, cancellationToken) =>
                {
                    await foreach (var status in state.client.WatchOperationAsync(state.location, cancellationToken))
                    {
                        if (status.IsTerminal)
                        {
                            return status;
                        }
                    }

                    throw new HttpRequestException($"The events of '{state.location}' ended before the operation's terminal status.");
                },
                (client: this, location));
            return ReadResult<TResult>(terminal);
        }

        var response = operation.Response;
        while (true)
        {
            // 服务器已按轮询请求的等待偏好完成等待，因此客户端立即发起下一次轮询。
            if (response.GetHeader(DurableHttpHeaders.PreferenceApplied) is null)
            {
                await DurableTask.Delay(GetPollDelay(response, options));
            }

            response = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, location);
                if (options.Wait > TimeSpan.Zero)
                {
                    request.Headers.TryAddWithoutValidation(DurableHttpHeaders.Prefer, $"wait={(long)options.Wait.TotalSeconds}");
                }

                return request;
            });

            // 一次轮询发生暂时性故障（例如 429 Too Many Requests 或 503 Service Unavailable）不会改变
            // 操作状态，因此下次轮询遵循其 Retry-After。
            if (IsTransient(response.StatusCode))
            {
                continue;
            }

            if (TryComplete(response, operation.IsStatusMonitor, out TResult result))
            {
                return result;
            }
        }
    }

    /// <summary>读取结束操作的响应结果；若操作仍在运行，则返回 <see langword="false"/>。</summary>
    private bool TryComplete<TResult>(DurableHttpResponse response, bool isStatusMonitorResponse, out TResult result)
    {
        result = default!;
        if (isStatusMonitorResponse && response.IsSuccessStatusCode)
        {
            // 无论轮询状态监视器得到哪个成功状态码，其响应体均报告操作状态。
            var operation = DurableOperation.Read(response.Content.Span);
            if (!operation.IsTerminal)
            {
                return false;
            }

            result = ReadResult<TResult>(operation);
            return true;
        }

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            return false;
        }

        if (!isStatusMonitorResponse && response.IsSuccessStatusCode)
        {
            result = response.ReadFromJson<TResult>(_json)!;
            return true;
        }

        if (!isStatusMonitorResponse && response.StatusCode == HttpStatusCode.FailedDependency)
        {
            result = ReadResult<TResult>(DurableOperation.Read(response.Content.Span));
            return true;
        }

        throw response.CreateException();
    }

    private TResult ReadResult<TResult>(DurableOperation operation) => operation.Status switch
    {
        DurableOperationStatus.Succeeded => operation.Result is { ValueKind: not JsonValueKind.Null } result ? result.Deserialize(_json.GetTypeInfo<TResult>())! : default!,
        var status => throw new DurableHttpOperationException(
            status is DurableOperationStatus.Canceled ? status : DurableOperationStatus.Failed,
            operation.Error?.Code,
            operation.Error?.Message),
    };

    private static async DurableTask<DurableOperation> ReadOperationAsync(DurableHttpClient client, Func<HttpRequestMessage> createRequest)
    {
        var response = (await client.SendAsync(createRequest)).EnsureSuccessStatusCode();
        return DurableOperation.Read(response.Content.Span);
    }

    /// <summary>
    /// 返回下次轮询前的等待时长：取响应中的 <c>Retry-After</c>，至少为
    /// <see cref="DurableHttpOperationOptions.PollInterval"/>，至多为 <see cref="DurableHttpOperationOptions.MaxPollInterval"/>，
    /// 避免服务器给出的过短或已过期的 <c>Retry-After</c> 使轮询快于任务设定的频率，也避免过长的值使任务停滞。
    /// </summary>
    internal static TimeSpan GetPollDelay(DurableHttpResponse response, DurableHttpOperationOptions options)
    {
        TimeSpan? retryAfter = RetryConditionHeaderValue.TryParse(response.GetHeader("Retry-After"), out var value)
            ? value.Delta ?? value.Date - DurableTask.Current.UtcNow
            : null;
        var max = options.MaxPollInterval > options.PollInterval ? options.MaxPollInterval : options.PollInterval;
        return retryAfter is not { } delay || delay < options.PollInterval ? options.PollInterval : delay > max ? max : delay;
    }

    /// <summary>返回轮询状态是否表示暂时性故障；此类故障之后会重试轮询。</summary>
    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>将 <paramref name="suffix"/> 追加至 <paramref name="location"/> 的路径末尾，同时保留其查询字符串。</summary>
    private static Uri Append(Uri location, string suffix) => new(location.GetLeftPart(UriPartial.Path) + suffix + location.Query);
}
