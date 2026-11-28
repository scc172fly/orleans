using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>格式化和读取 Nexus 操作令牌，该令牌指定 Nexus 服务中的操作。</summary>
/// <remarks>
/// Nexus 调用方通过令牌取消操作，因此令牌携带操作标识符。可在服务集合中注册一个
/// 实现来替换默认实现 <see cref="PlainTextNexusOperationTokenFormat"/>，
/// 例如使用能够签名或加密令牌的实现。
/// </remarks>
public interface INexusOperationTokenFormat
{
    /// <summary>返回操作 <paramref name="operationId"/> 的令牌。</summary>
    /// <param name="operationId">操作标识符，由调用方通过 <c>Nexus-Request-Id</c> 选定。</param>
    /// <returns>由 HTTP 请求头值允许的字符组成的令牌。</returns>
    string Format(string operationId);

    /// <summary>读取 <see cref="Format"/> 返回的令牌。</summary>
    /// <param name="token">令牌。</param>
    /// <param name="operationId">操作标识符。</param>
    /// <returns><see langword="true"/> 表示 <paramref name="token"/> 是有效令牌。</returns>
    bool TryParse(string token, [NotNullWhen(true)] out string? operationId);
}

/// <summary>将 Nexus 操作令牌格式化为操作标识符本身。</summary>
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
/// <summary>定义 Nexus 协议使用的 HTTP 请求头、响应头及状态名称。</summary>
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

    /// <summary>返回 Nexus <c>HandlerError</c> 类型的响应，错误类型为 <paramref name="type"/>，例如 <c>NOT_FOUND</c>。</summary>
    public static IResult HandlerError(int statusCode, string type, string message) => TypedResults.Json(
        new NexusFailure(message, new() { ["type"] = HandlerErrorType }, new() { ["type"] = type }),
        NexusJsonContext.Default.NexusFailure,
        statusCode: statusCode);
}

/// <summary>Nexus 的 <c>Failure</c>。</summary>
internal sealed record NexusFailure(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("metadata")] Dictionary<string, string> Metadata,
    [property: JsonPropertyName("details")] Dictionary<string, string> Details);

/// <summary>Nexus 的 <c>OperationInfo</c>。</summary>
internal sealed record NexusOperationInfo(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("state")] string State);

[JsonSerializable(typeof(NexusFailure))]
[JsonSerializable(typeof(NexusOperationInfo))]
internal sealed partial class NexusJsonContext : JsonSerializerContext;

/// <summary>标识端点所属的 Nexus 服务。</summary>
/// <param name="Scope">服务操作的作用域。</param>
internal sealed record NexusServiceMetadata(string Scope)
{
    /// <summary>返回请求端点所属的 Nexus 服务。</summary>
    public static NexusServiceMetadata Get(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<NexusServiceMetadata>()
        ?? throw new InvalidOperationException(
            $"Nexus operation endpoints belong to a group which MapNexusService maps, but '{context.GetEndpoint()?.DisplayName}' doesn't.");

    /// <summary>返回请求调用方的操作 <paramref name="operationId"/> 的键。</summary>
    public string GetKey(HttpContext context, DurableHttpOptions options, string operationId) =>
        DurableOperationKeys.Complete(TaskId.CreateRoot(Scope), context, options, operationId);
}
