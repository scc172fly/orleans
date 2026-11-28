using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Orleans.Durable.Http;

/// <summary>
/// 遵循 Azure 长时间运行操作的约定提供持久化操作服务：状态监视器位于
/// <c>{group}/operations/{operationId}</c> 下，并使用 <c>Operation-Location</c>、<c>Retry-After</c> 和 <c>Prefer: wait</c>。
/// </summary>
internal sealed class DurableHttpOperations(
    DurableOperationService operations,
    DurableCallbackRegistrar callbacks,
    TemplateBinderFactory binders,
    EndpointDataSource endpoints,
    IOptions<DurableHttpOptions> options,
    IOptions<HttpJsonOptions> jsonOptions)
{
    /// <summary>标识操作的路由参数。</summary>
    public const string OperationIdParameter = "operationId";

    /// <summary>携带操作状态的服务器发送事件的类型。</summary>
    private const string StatusEvent = "status";

    /// <summary>用于保持事件流连接且不携带数据的服务器发送事件的类型。</summary>
    private const string PingEvent = "ping";

    private readonly DurableHttpOptions _options = options.Value;
    private readonly JsonSerializerOptions _json = jsonOptions.Value.SerializerOptions;

    public DurableHttpOptions Options => _options;

    /// <summary>
    /// 启动 <paramref name="work"/>，将其作为操作 <paramref name="operationId"/>，按请求指定的偏好等待，
    /// 并返回操作结果或其状态监视器。通过
    /// <c>Operation-Callback</c> 请求头注册回调的请求，会在正在运行的操作进入终态时在该地址收到状态监视器。
    /// </summary>
    public async Task<IResult> StartAsync(HttpContext context, DurableOperationsMetadata group, DurableTask work, string operationId)
    {
        if (!callbacks.TryRead(
            context,
            context.Request.Headers[DurableHttpHeaders.OperationCallback].ToString(),
            DurableHttpHeaders.OperationCallbackHeaderPrefix,
            out var callback,
            out var callbackError))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Operation-Callback is not accepted", detail: callbackError);
        }

        DurableTaskResponse response;
        var key = group.GetKey(context, endpoints, _options, operationId);
        var wait = DurableHttpRequestHeaders.GetWait(context.Request.Headers, _options.MaxWait);
        try
        {
            response = await operations.StartAsync(key, work, wait, context.RequestAborted);
        }
        catch (DurableTaskConflictException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Idempotency-Key is already used",
                detail: "The Idempotency-Key identifies an operation which another request started. A key is reused only to retry the same request.");
        }
        catch (DurableOperationUnavailableException)
        {
            return Unavailable(context);
        }

        var location = GetOperationLocation(context, group, operationId);
        context.Response.Headers[DurableHttpHeaders.OperationLocation] = location;
        if (callback is not null && !response.IsCompleted)
        {
            await callbacks.RegisterAsync(callback, DurableCallbackProtocol.Webhook, operations.GetHostId(key), operationId, location, context.RequestAborted);
        }

        if (wait > TimeSpan.Zero)
        {
            context.Response.Headers[DurableHttpHeaders.PreferenceApplied] = $"wait={(int)wait.TotalSeconds}";
        }

        return response.Status switch
        {
            DurableTaskStatus.CompletedSuccessfully when response.ResultType is { } resultType =>
                Results.Json(response.Result, _json.GetTypeInfo(resultType)),
            DurableTaskStatus.CompletedSuccessfully => TypedResults.NoContent(),
            DurableTaskStatus.Failed or DurableTaskStatus.Canceled =>
                OperationResult(context, operationId, response, StatusCodes.Status424FailedDependency),
            _ => OperationResult(context, operationId, response, StatusCodes.Status202Accepted),
        };
    }

    /// <summary>在操作进入终态或请求偏好的等待时间到期时，返回操作的状态监视器。</summary>
    public Task<IResult> GetAsync(HttpContext context) => HandleAsync(context, async (operation, cancellationToken) =>
    {
        var wait = DurableHttpRequestHeaders.GetWait(context.Request.Headers, _options.MaxWait);
        var response = await operations.PollAsync(operation.Key, wait, cancellationToken);
        return OperationResult(context, operation.Id, response, StatusCodes.Status200OK);
    });

    /// <summary>
    /// 以服务器发送事件的形式传输操作状态：首先传输当前状态，若操作正在运行，则等待其进入
    /// 终态后传输终态，随后结束流。各事件的标识符为其状态。
    /// </summary>
    /// <remarks>
    /// <c>EventSource</c> 在流结束时携带最后一个事件标识符进行重连，因此在
    /// 终态之后重连会收到 <c>204 No Content</c>，使其停止重连。
    /// </remarks>
    public Task<IResult> WatchAsync(HttpContext context) => HandleAsync(context, async (operation, cancellationToken) =>
    {
        if (Enum.TryParse<DurableOperationStatus>(context.Request.Headers["Last-Event-ID"], out var lastStatus)
            && lastStatus is DurableOperationStatus.Succeeded or DurableOperationStatus.Failed or DurableOperationStatus.Canceled)
        {
            return TypedResults.NoContent();
        }

        var response = await operations.PollAsync(operation.Key, TimeSpan.Zero, cancellationToken);
        return new DurableOperationEventsResult(WatchAsync(operation, response, cancellationToken));
    });

    /// <summary>
    /// 持久化地请求取消正在运行的操作，携带 JSON 请求体中可选的 <c>reason</c>，
    /// 并返回其状态监视器：正在运行的操作返回 <c>202 Accepted</c>，已处于终态的操作返回 <c>200 OK</c>，
    /// 且终态操作保持不变。
    /// </summary>
    public async Task<IResult> CancelAsync(HttpContext context)
    {
        var (request, error) = await ReadBodyAsync(context, DurableHttpJsonContext.Default.DurableOperationCancelRequest, isRequired: false);
        return error ?? await HandleAsync(context, async (operation, cancellationToken) =>
        {
            var response = await operations.CancelAsync(operation.Key, request?.Reason, cancellationToken);
            return OperationResult(context, operation.Id, response, response.IsCompleted ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
        });
    }

    /// <summary>向操作的任务传递 <paramref name="signal"/>，其值为请求的 JSON 请求体。</summary>
    public async Task<IResult> SignalAsync<T>(HttpContext context, DurableSignal<T> signal)
    {
        var (value, error) = await ReadBodyAsync(context, _json.GetTypeInfo<T>(), isRequired: true);
        return error ?? await HandleAsync(context, async (operation, cancellationToken) =>
            await operations.SignalAsync(operation.Key, signal, value!, cancellationToken) switch
            {
                DurableSignalResult.Accepted or DurableSignalResult.Duplicate => TypedResults.NoContent(),
                DurableSignalResult.TaskNotFound => OperationNotFound(),
                DurableSignalResult.TaskCompleted => TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The operation has completed",
                    detail: $"The operation completed without receiving signal '{signal.Name}'."),
                var result => TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The signal has another value",
                    detail: $"The operation holds a different value for signal '{signal.Name}' ({result})."),
            });
    }

    /// <summary>
    /// 按 Minimal API 绑定 <c>[FromBody]</c> 参数的方式读取 JSON 请求体：请求体的内容类型不符时
    /// 返回 <c>415 Unsupported Media Type</c>，JSON 无效、必需的请求体缺失或值为
    /// <c>null</c> 时返回 <c>400 Bad Request</c>。
    /// </summary>
    /// <remarks>
    /// 根据请求是否包含请求体来判断是否发送了值，因此不会将缺失的请求体读取为类型的
    /// 默认值，例如 <see langword="false"/>（对于 <see cref="bool"/> 类型）。
    /// </remarks>
    private static async Task<(T? Value, IResult? Error)> ReadBodyAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo, bool isRequired)
    {
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != true)
        {
            return isRequired ? (default, BadBody($"The request has no body, which holds the {typeof(T).Name} value.")) : (default, null);
        }

        if (!context.Request.HasJsonContentType())
        {
            return (default, TypedResults.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "The request body isn't JSON",
                detail: "The request's Content-Type is application/json, or another JSON media type."));
        }

        T? value;
        try
        {
            value = await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted);
        }
        catch (JsonException)
        {
            return (default, BadBody($"The request body isn't a valid JSON {typeof(T).Name}."));
        }

        return value is null && isRequired ? (default, BadBody($"The request body holds no {typeof(T).Name} value.")) : (value, null);

        static IResult BadBody(string detail) =>
            TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request body is invalid", detail: detail);
    }

    /// <summary>
    /// 使用 <paramref name="handler"/> 处理指定操作的请求；若操作不存在，则返回 <c>404 Not Found</c>；
    /// 若请求未到达操作，则返回 <c>503 Service Unavailable</c>。
    /// </summary>
    private async Task<IResult> HandleAsync(HttpContext context, Func<Operation, CancellationToken, Task<IResult>> handler)
    {
        if (context.GetRouteValue(OperationIdParameter) is not string operationId || !DurableHttpRequestHeaders.IsValidOperationId(operationId))
        {
            return OperationNotFound();
        }

        try
        {
            var key = DurableOperationsMetadata.Get(context).GetKey(context, endpoints, _options, operationId);
            return await handler(new(operationId, key), context.RequestAborted);
        }
        catch (DurableTaskNotFoundException)
        {
            return OperationNotFound();
        }
        catch (DurableOperationUnavailableException)
        {
            return Unavailable(context);
        }
    }

    private async IAsyncEnumerable<SseItem<DurableOperation>> WatchAsync(
        Operation operation,
        DurableTaskResponse response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var status = ToOperation(operation.Id, response);
        yield return new(status, StatusEvent) { EventId = status.Status.ToString(), ReconnectionInterval = _options.RetryAfter };
        if (status.IsTerminal)
        {
            yield break;
        }

        while (true)
        {
            response = await operations.PollAsync(operation.Key, DurableOperationService.MaxPollInterval, cancellationToken);
            if (response.IsCompleted)
            {
                break;
            }

            // 每次轮询后的心跳都会向连接写入数据，因此当客户端已放弃流时，该流在
            // 写入时结束，而不是持续轮询直至操作完成。
            yield return new(status, PingEvent);
        }

        status = ToOperation(operation.Id, response);
        yield return new(status, StatusEvent) { EventId = status.Status.ToString() };
    }

    private IResult OperationResult(HttpContext context, string operationId, DurableTaskResponse response, int statusCode)
    {
        if (!response.IsCompleted)
        {
            SetRetryAfter(context);
        }

        return TypedResults.Json(ToOperation(operationId, response), DurableHttpJsonContext.Default.DurableOperation, statusCode: statusCode);
    }

    private DurableOperation ToOperation(string operationId, DurableTaskResponse response) =>
        DurableOperationResponses.ToOperation(operationId, response, _json, _options);

    /// <summary>
    /// 对于未改变操作的请求（例如请求未到达操作对应的 Grain），返回 <c>503 Service Unavailable</c>，
    /// 并携带 <c>Retry-After</c> 响应头，使客户端重新发送请求。
    /// </summary>
    private IResult Unavailable(HttpContext context)
    {
        SetRetryAfter(context);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The operation is unavailable",
            detail: "The request didn't reach the operation, which it left unchanged. Send the request again with the same Idempotency-Key.");
    }

    private void SetRetryAfter(HttpContext context) =>
        context.Response.Headers.RetryAfter = ((long)Math.Ceiling(_options.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 返回操作状态监视器的 URL，该 URL 位于请求端点所属组内，并使用请求中的
    /// 组路由参数值。
    /// </summary>
    private string GetOperationLocation(HttpContext context, DurableOperationsMetadata group, string operationId)
    {
        var path = group.GetOperationPath(context, endpoints, binders, operationId)
            ?? throw new InvalidOperationException(
                $"The route values of '{context.GetEndpoint()?.DisplayName}' don't locate its group's operations. A durable task endpoint uses the route parameters of the group which WithDurableOperations configures.");
        var request = context.Request;
        return UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, new PathString(path));
    }
    private static IResult OperationNotFound() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The operation was not found",
        detail: "The operation doesn't exist, or its retention has ended.");

    /// <summary>标识请求所指定的操作。</summary>
    /// <param name="Id">请求指定的操作标识符。</param>
    /// <param name="Key">操作的键。</param>
    private readonly record struct Operation(string Id, string Key);

    /// <summary>以 <c>text/event-stream</c> 响应写入操作状态。</summary>
    private sealed class DurableOperationEventsResult(IAsyncEnumerable<SseItem<DurableOperation>> events) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache,no-store";
            response.Headers.Pragma = "no-cache";
            response.Headers.ContentEncoding = "identity";
            httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();
            await SseFormatter.WriteAsync(
                events,
                response.Body,
                static (item, writer) =>
                {
                    if (item.EventType == PingEvent)
                    {
                        return;
                    }

                    using var json = new Utf8JsonWriter(writer);
                    JsonSerializer.Serialize(json, item.Data, DurableHttpJsonContext.Default.DurableOperation);
                },
                httpContext.RequestAborted);
        }
    }
}
