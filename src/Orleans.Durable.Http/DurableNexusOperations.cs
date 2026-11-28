using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Orleans.Durable.Http;

/// <summary>按 Nexus 协议提供持久化操作服务：通过操作令牌寻址，支持启动和取消操作。</summary>
/// <remarks>参见 <see href="https://github.com/nexus-rpc/api/blob/main/SPEC.md">Nexus HTTP 规范</see>。</remarks>
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
    /// 启动 <paramref name="work"/>，将其作为由请求的 <c>Nexus-Request-Id</c> 命名的操作；当操作
    /// 进入终态时返回结果，运行期间则返回操作令牌。
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
    /// 持久化地请求取消由请求令牌指定的操作，并返回 <c>202 Accepted</c>；
    /// 对于已进入终态的操作同样如此，且不改变该操作。
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

    /// <summary>对于未改变操作的请求，返回 <c>UNAVAILABLE</c> 处理程序错误，使调用方重试。</summary>
    private static IResult Unavailable() =>
        NexusProtocol.HandlerError(
            StatusCodes.Status503ServiceUnavailable,
            "UNAVAILABLE",
            "The request didn't reach the operation, which it left unchanged. Send the request again with the same Nexus-Request-Id.");
}
