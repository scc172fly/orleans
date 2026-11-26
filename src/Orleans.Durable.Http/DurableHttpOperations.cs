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
/// 使用 Azure 长期作业公约的持久业务:
/// <c>{group}/operations/{operationId}</c>、 <c>Operation-Location</c>、 <c>Retry-After</c>之后重新转调,以及 <c>Prefer: wait</c>。
/// </summary>
internal sealed class DurableHttpOperations(
    DurableOperationService operations,
    DurableCallbackRegistrar callbacks,
    TemplateBinderFactory binders,
    EndpointDataSource endpoints,
    IOptions<DurableHttpOptions> options,
    IOptions<HttpJsonOptions> jsonOptions)
{
    /// <summary>指一个操作的路线参数。</summary>
    public const string OperationIdParameter = "operationId";

    /// <summary>包含操作状态的服务器- 发送事件类型 。</summary>
    private const string StatusEvent = "status";

    /// <summary>服务器- 发送事件类型, 使事件流的连接保持生命状态, 没有数据 。</summary>
    private const string PingEvent = "ping";

    private readonly DurableHttpOptions _options = options.Value;
    private readonly JsonSerializerOptions _json = jsonOptions.Value.SerializerOptions;

    public DurableHttpOptions Options => _options;

    /// <summary>
    /// 开始 <paramref name="work"/> 作为操作 <paramref name="operationId"/>, 等待它, 请求所偏爱的,
    /// 将调回
    /// <c>Operation-Callback</c> 头在运行中的操作为终端时收到状态监视器。
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

    /// <summary>一旦业务处于终端状态或请求首选等待时间已过, 即返回运行状态监视器。</summary>
    public Task<IResult> GetAsync(HttpContext context) => HandleAsync(context, async (operation, cancellationToken) =>
    {
        var wait = DurableHttpRequestHeaders.GetWait(context.Request.Headers, _options.MaxWait);
        var response = await operations.PollAsync(operation.Key, wait, cancellationToken);
        return OperationResult(context, operation.Id, response, StatusCodes.Status200OK);
    });

    /// <summary>
    /// 将业务作为服务器待发事件:其现状,然后是一旦处于终端状态
    /// 运行中, 流结束之后。 每个事件的身份识别符就是状态 。
    /// </summary>
    /// <remarks>
    /// A 音 A <c>EventSource</c> 当流流结束时, 带有最后一个事件标识符, 所以在
    /// 终端状态接收 <c>204 No Content</c>, 结束其连接 。
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
    /// 可操作性地请求取消运行中的操作, 携带可选 <c>reason</c> 提出请求的日期 JSON 体,
    /// 返回其状态监视器: <c>202 Accepted</c> , <c>200 OK</c> 一个终端,
    /// 不变。
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

    /// <summary>交付交付 <paramref name="signal"/> 请求书的价值 JSON 操作的任务的物体。</summary>
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
    /// 将请求的 JSON 正文读为最小 API 绑定 <c>[FromBody]</c> 参数:另一类型内容的体
    /// 返回 <c>415 Unsupported Media Type</c>, 和无效的体 JSON,或缺失的所需体或
    /// 持有 <c>null</c>,返回 <c>400 Bad Request</c>。
    /// </summary>
    /// <remarks>
    /// 请求是否有一个身体来决定是否发送了值, 所以缺少的遗体从不读作类型
    /// 默认缺省,例如 <see langword="false"/> a 的a <see cref="bool"/>.
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
    /// 处理一个请求, 其中指定操作名称 <paramref name="handler"/>返回 <c>404 Not Found</c> 用于 a
    /// 操作中不存在的, <c>503 Service Unavailable</c> 请求没有达到的
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

            // 每一次民意调查之后的一瞬间 写到这段关联 所以一个客户抛弃的流
            // 而不是为行动持续时间进行投票
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
    /// 返回 <c>503 Service Unavailable</c> ,因为一项请求已停止运行,例如没有
    /// 到达操作 grains,使用一个 <c>Retry-After</c> 页眉,客户端再发送一次。
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
    /// 返回运行状态监视器的 URL, 在请求的端点组中, 以请求的值返回
    /// 组的路线参数。
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

    /// <summary>标明请求方名称的操作。</summary>
    /// <param name="Id">请求中的名称为操作识别符。</param>
    /// <param name="Key">操作的密钥。</param>
    private readonly record struct Operation(string Id, string Key);

    /// <summary>以a. <c>text/event-stream</c> 答复。</summary>
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
