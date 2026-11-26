using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>运行 HTTP 请求和业务,作为与指定 <see cref="HttpClient"/>.</summary>
/// <remarks>
/// 指定客户的处理器从 <see cref="DurableIdempotencyKeyHandler"/>开始,使每个步骤不安全
/// 要求使用 <c>Idempotency-Key</c>。
/// </remarks>
internal sealed class DurableHttpClient(IHttpClientFactory clientFactory, string name, DurableHttpClientOptions options) : IDurableHttpClient
{
    /// <summary>不作广告的事件流作为浏览器 <c>EventSource</c> 的</summary>用途而使用的重连间隔。
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
                // 客户端缓冲回复内容,最多不超过 DurableHttpClientOptions.MaxResponseContentLength。
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
                    // 服务器无法访问服务器, 所以在间隔后重新连接, 作为 EventSource 确实如此。
                    response = null;
                }

                if (response is not null)
                {
                    using (response)
                    {
                        // 服务器在终端状态后回答重连204无内容。
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

                        // 流在操作终端状态之前结束, 例如代理关闭时, 代理关闭时结束, 所以
                        // 服务器广告间隔后, 与最后一个事件的身份识别符重新连接 。
                        lastEventId = parser.LastEventId is { Length: > 0 } id ? id : lastEventId;
                        reconnectionInterval = parser.ReconnectionInterval is var interval && interval > TimeSpan.Zero && interval != Timeout.InfiniteTimeSpan
                            ? interval
                            : reconnectionInterval;
                    }
                }
            }

            await Task.Delay(reconnectionInterval, cancellationToken);
        }

        // 读取下一个事件,或者在流结束时返回错误,包括连接中流下降时。
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

        // 运行中的操作总是有一个位置, FromStart 能够建立。
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
            // 应用民意测验的等待优先选择的服务器已经等待, 因此下一次民意测验会同时进行。
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

            // 一次民意调查的短暂失败,如429项请求过多或503项服务无可用,使
            // 之后的民意测验
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

    /// <summary>读取完成操作或返回运行期间的 <see langword="false"/> 响应的结果。</summary>
    private bool TryComplete<TResult>(DurableHttpResponse response, bool isStatusMonitorResponse, out TResult result)
    {
        result = default!;
        if (isStatusMonitorResponse && response.IsSuccessStatusCode)
        {
            // 状况监测员报告该机构的运作,无论成功状态代码对调查做出何种答复。
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
    /// 返回下次民调前的延迟时间:至少回复 <c>Retry-After</c>
    /// <see cref="DurableHttpOperationOptions.PollInterval"/> 和最多 <see cref="DurableHttpOperationOptions.MaxPollInterval"/>,
    /// 服务器的短或过去 <c>Retry-After</c> 调查的速度不会比任务选择的快 很长的任务也不会拖住它
    /// </summary>
    internal static TimeSpan GetPollDelay(DurableHttpResponse response, DurableHttpOperationOptions options)
    {
        TimeSpan? retryAfter = RetryConditionHeaderValue.TryParse(response.GetHeader("Retry-After"), out var value)
            ? value.Delta ?? value.Date - DurableTask.Current.UtcNow
            : null;
        var max = options.MaxPollInterval > options.PollInterval ? options.MaxPollInterval : options.PollInterval;
        return retryAfter is not { } delay || delay < options.PollInterval ? options.PollInterval : delay > max ? max : delay;
    }

    /// <summary>返回民意测验是否报告暂时失败,然后重新进行调查。</summary>
    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>附录 <paramref name="suffix"/> 和 <paramref name="location"/>,保留其查询。</summary>
    private static Uri Append(Uri location, string suffix) => new(location.GetLeftPart(UriPartial.Path) + suffix + location.Query);
}
