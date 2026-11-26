using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Orleans.Durable.Http;

/// <summary>用于使用 Nexus 协议的耐久操作: 启动和取消, 由运行符号处理 。</summary>
/// <remarks>见见 <see href="https://github.com/nexus-rpc/api/blob/main/SPEC.md">Nexus HTTP 规格</see>.</remarks>
internal sealed class DurableNexusOperations(
    DurableOperationService operations,
    DurableCallbackRegistrar callbacks,
    INexusOperationTokenFormat tokens,
    IOptions<DurableHttpOptions> options,
    IOptions<HttpJsonOptions> jsonOptions)
{
    private readonly DurableHttpOptions _options = options.Value;
    private readonly JsonSerializerOptions _json = jsonOptions.Value.SerializerOptions;

    /// <summary>
    /// 开始 <paramref name="work"/> 请求所请求的业务 <c>Nexus-Request-Id</c> 返回其
    /// 当它运行时是终点或标记时,
    /// </summary>
    public async Task<IResult> StartAsync(HttpContext context, DurableTask work)
    {
        var requestId = context.Request.Headers[NexusProtocol.RequestId];
        var operationId = requestId.Count == 0 ? Guid.NewGuid().ToString("N") : requestId.ToString();
        if (requestId.Count > 1 || !DurableHttpRequestHeaders.IsValidOperationId(operationId))
        {
            return NexusProtocol.HandlerError(
                StatusCodes.Status400BadRequest,
                "BAD_REQUEST",
                $"A Nexus-Request-Id has 1 to {DurableHttpRequestHeaders.MaxIdempotencyKeyLength} printable ASCII characters other than '/', and isn't '.' or '..'.");
        }


        if (!callbacks.TryRead(
            context,
            context.Request.Query[NexusProtocol.CallbackQuery].ToString(),
            NexusProtocol.CallbackHeaderPrefix,
            out var callback,
            out var callbackError))
        {
            return NexusProtocol.HandlerError(StatusCodes.Status400BadRequest, "BAD_REQUEST", callbackError!);
        }

        DurableTaskResponse response;
        var key = NexusServiceMetadata.Get(context).GetKey(context, _options, operationId);
        try
        {
            response = await operations.StartAsync(key, work, TimeSpan.Zero, context.RequestAborted);
        }
        catch (DurableTaskConflictException)
        {
            return NexusProtocol.HandlerError(
                StatusCodes.Status409Conflict,
                "CONFLICT",
                "The Nexus-Request-Id identifies an operation which another request started.");
        }
        catch (DurableOperationUnavailableException)
        {
            return Unavailable();
        }

        switch (response.Status)
        {
            case DurableTaskStatus.CompletedSuccessfully:
                context.Response.Headers[NexusProtocol.OperationState] = NexusProtocol.Succeeded;
                return response.ResultType is { } resultType ? Results.Json(response.Result, _json.GetTypeInfo(resultType)) : TypedResults.Ok();
            case DurableTaskStatus.Failed or DurableTaskStatus.Canceled:
                return TypedResults.Json(
                    DurableOperationResponses.ToNexusFailure(response, _options),
                    NexusJsonContext.Default.NexusFailure,
                    statusCode: StatusCodes.Status424FailedDependency);
            default:
                var token = tokens.Format(operationId);
                if (callback is not null)
                {
                    await callbacks.RegisterAsync(callback, DurableCallbackProtocol.Nexus, operations.GetHostId(key), token, operationLocation: null, context.RequestAborted);
                }

                return TypedResults.Json(
                    new NexusOperationInfo(token, NexusProtocol.Running),
                    NexusJsonContext.Default.NexusOperationInfo,
                    statusCode: StatusCodes.Status201Created);
        }
    }

    /// <summary>
    /// 可持久地请求取消该请求的象征性名称,并返回 <c>202 Accepted</c>,
    /// 包括终端操作,但无变化。
    /// </summary>
    public async Task<IResult> CancelAsync(HttpContext context)
    {
        var header = context.Request.Headers[NexusProtocol.OperationToken];
        var token = header.Count > 0 ? header.ToString() : context.Request.Query[NexusProtocol.TokenQuery].ToString();
        if (!tokens.TryParse(token, out var operationId))
        {
            return NotFound();
        }

        try
        {
            var key = NexusServiceMetadata.Get(context).GetKey(context, _options, operationId);
            await operations.CancelAsync(key, reason: null, context.RequestAborted);
        }
        catch (DurableTaskNotFoundException)
        {
            return NotFound();
        }
        catch (DurableOperationUnavailableException)
        {
            return Unavailable();
        }

        return TypedResults.Accepted((string?)null);
    }

    private static IResult NotFound() =>
        NexusProtocol.HandlerError(StatusCodes.Status404NotFound, "NOT_FOUND", "The operation doesn't exist, or its retention has ended.");

    /// <summary>回返a <c>UNAVAILABLE</c> 处理器错误, 请求时将操作保留原样, 调用者重试 。</summary>
    private static IResult Unavailable() =>
        NexusProtocol.HandlerError(
            StatusCodes.Status503ServiceUnavailable,
            "UNAVAILABLE",
            "The request didn't reach the operation, which it left unchanged. Send the request again with the same Nexus-Request-Id.");
}
