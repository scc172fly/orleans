using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>格式和读取 Nexus 操作符,该符名为 Nexus 服务的操作。</summary>
/// <remarks>
/// Nexus 呼叫器以牌照取消操作, 所以该牌照带有操作的识别符。 请注册一个
/// 用于替换默认值的服务收藏中的执行 <see cref="PlainTextNexusOperationTokenFormat"/>,
/// 例如,用那些能显示迹象或能加密的符号。
/// </remarks>
public interface INexusOperationTokenFormat
{
    /// <summary>返回操作的标记 <paramref name="operationId"/>。</summary>
    /// <param name="operationId">操作识别符, 由调用器选择 <c>Nexus-Request-Id</c>.</param>
    /// <returns>HTTP 信头值中有效的字符符号。</returns>
    string Format(string operationId);

    /// <summary>读取返回的 <see cref="Format"/> 符号。</summary>
    /// <param name="token">牌子。</param>
    /// <param name="operationId">操作识别符。</param>
    /// <returns><see langword="true"/> 何时 <paramref name="token"/> 是一个有效的符号。</returns>
    bool TryParse(string token, [NotNullWhen(true)] out string? operationId);
}

/// <summary>格式化 Nexus 操作符号作为操作的标识符本身。</summary>
public sealed class PlainTextNexusOperationTokenFormat : INexusOperationTokenFormat
{
    /// <inheritdoc />
    public string Format(string operationId)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        return operationId;
    }

    /// <inheritdoc />
    public bool TryParse(string token, [NotNullWhen(true)] out string? operationId)
    {
        ArgumentNullException.ThrowIfNull(token);
        operationId = DurableHttpRequestHeaders.IsValidOperationId(token) ? token : null;
        return operationId is not null;
    }
}
/// <summary>命名 HTTP 页眉和Nexus 协议的状态。</summary>
internal static class NexusProtocol
{
    public const string RequestId = "Nexus-Request-Id";
    public const string OperationToken = "Nexus-Operation-Token";
    public const string OperationState = "Nexus-Operation-State";
    public const string OperationCloseTime = "Nexus-Operation-Close-Time";
    public const string TokenQuery = "token";
    public const string CallbackQuery = "callback";
    public const string CallbackHeaderPrefix = "Nexus-Callback-";

    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    public const string OperationErrorType = "nexus.OperationError";
    public const string HandlerErrorType = "nexus.HandlerError";

    /// <summary>返回 nexus <c>HandlerError</c> 答复和回应 <paramref name="type"/>,例如 <c>NOT_FOUND</c>.</summary>
    public static IResult HandlerError(int statusCode, string type, string message) => TypedResults.Json(
        new NexusFailure(message, new() { ["type"] = HandlerErrorType }, new() { ["type"] = type }),
        NexusJsonContext.Default.NexusFailure,
        statusCode: statusCode);
}

/// <summary>A 内反点 <c>Failure</c>.</summary>
internal sealed record NexusFailure(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("metadata")] Dictionary<string, string> Metadata,
    [property: JsonPropertyName("details")] Dictionary<string, string> Details);

/// <summary>A 内反点 <c>OperationInfo</c>.</summary>
internal sealed record NexusOperationInfo(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("state")] string State);

[JsonSerializable(typeof(NexusFailure))]
[JsonSerializable(typeof(NexusOperationInfo))]
internal sealed partial class NexusJsonContext : JsonSerializerContext;

/// <summary>确定终点的 Nexus 服务。</summary>
/// <param name="Scope">服务业务的范围。</param>
internal sealed record NexusServiceMetadata(string Scope)
{
    /// <summary>返回请求端点的 Nexus 服务。</summary>
    public static NexusServiceMetadata Get(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<NexusServiceMetadata>()
        ?? throw new InvalidOperationException(
            $"Nexus operation endpoints belong to a group which MapNexusService maps, but '{context.GetEndpoint()?.DisplayName}' doesn't.");

    /// <summary>返回操作的密钥 <paramref name="operationId"/> 请求人打电话时的号码。</summary>
    public string GetKey(HttpContext context, DurableHttpOptions options, string operationId) =>
        DurableOperationKeys.Complete(TaskId.CreateRoot(Scope), context, options, operationId);
}
