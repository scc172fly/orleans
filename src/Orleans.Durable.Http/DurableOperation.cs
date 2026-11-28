using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>
/// 表示操作的状态监视器，即用于报告持久化任务状态，并在任务进入
/// 终态后报告其结果或错误的资源。
/// </summary>
/// <param name="Id">用于寻址状态监视器的操作标识符。</param>
/// <param name="Status">操作的状态。</param>
/// <param name="Result">任务成功且有返回值时的结果。</param>
/// <param name="Error">任务失败或被取消时的失败或取消信息。</param>
/// <remarks>
/// 此文档采用 Azure 长时间运行操作状态监视器的结构，因此轮询 Azure 操作的客户端
/// 也能够读取它。持久化任务将其作为 <see cref="IDurableHttpClient"/> 步骤的结果记录。
/// </remarks>
[Alias("Orleans.Durable.Http.DurableOperation")]
public sealed record DurableOperation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] DurableOperationStatus Status,
    [property: JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Result = null,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DurableOperationError? Error = null)
{
    /// <summary>获取操作是否已进入终态。</summary>
    [JsonIgnore]
    public bool IsTerminal => Status is DurableOperationStatus.Succeeded or DurableOperationStatus.Failed or DurableOperationStatus.Canceled;

    /// <summary>
    /// 按 Azure 客户端的方式读取服务器返回的状态监视器：如果 <c>status</c> 不属于本包定义的状态，
    /// 例如 Azure Resource Manager 的 <c>InProgress</c> 或 <c>Accepted</c>，则将其读取为
    /// <see cref="DurableOperationStatus.Running"/>.
    /// </summary>
    internal static DurableOperation Read(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A status monitor is a JSON object.");
        }

        var status = ReadString(root, "status") is { } value && Enum.TryParse<DurableOperationStatus>(value, ignoreCase: true, out var parsed)
            ? parsed
            : DurableOperationStatus.Running;
        var result = root.TryGetProperty("result", out var element) && element.ValueKind != JsonValueKind.Null ? element.Clone() : (JsonElement?)null;
        var error = root.TryGetProperty("error", out element) && element.ValueKind == JsonValueKind.Object
            ? new DurableOperationError(ReadString(element, "code") ?? string.Empty, ReadString(element, "message") ?? string.Empty)
            : null;
        return new(ReadString(root, "id") ?? string.Empty, status, result, error);

        static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

/// <summary>为 Orleans 序列化 <see cref="DurableOperation"/>，将其结果表示为 JSON 文本。</summary>
[GenerateSerializer, Immutable]
internal readonly record struct DurableOperationSurrogate(
    string Id,
    DurableOperationStatus Status,
    string? Result,
    DurableOperationError? Error);

[RegisterConverter]
internal sealed class DurableOperationSurrogateConverter : IConverter<DurableOperation, DurableOperationSurrogate>
{
    public DurableOperation ConvertFromSurrogate(in DurableOperationSurrogate surrogate) => new(
        surrogate.Id,
        surrogate.Status,
        surrogate.Result is { } result ? ParseResult(result) : null,
        surrogate.Error);

    private static JsonElement ParseResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public DurableOperationSurrogate ConvertToSurrogate(in DurableOperation value) =>
        new(value.Id, value.Status, value.Result?.GetRawText(), value.Error);
}

/// <summary>标识操作的状态。</summary>
/// <remarks>这些值采用 Azure 长时间运行操作的状态，并按名称序列化。</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DurableOperationStatus>))]
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableOperationStatus")]
public enum DurableOperationStatus
{
    /// <summary>操作尚未启动。</summary>
    NotStarted,

    /// <summary>操作正在运行。持久化任务在进入终态前报告此状态。</summary>
    Running,

    /// <summary>操作已成功完成。</summary>
    Succeeded,

    /// <summary>操作失败。</summary>
    Failed,

    /// <summary>操作被取消。</summary>
    Canceled,
}

/// <summary>描述操作失败或被取消的原因。</summary>
/// <param name="Code">机器可读的错误码，由 <see cref="DurableHttpOptions.CreateError"/> 选择。</param>
/// <param name="Message">供人阅读的说明。</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableOperationError")]
public sealed record DurableOperationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// 失败操作的错误码，表示工作从未运行，因为执行工作的 Grain 在未存储该工作时便拒绝了它，
    /// 例如该工作标识符已在该 Grain 中用于另一个方法的任务。客户端可使用新的
    /// <c>Idempotency-Key</c> 重新启动工作。
    /// </summary>
    public const string NotStartedCode = "NotStarted";

    /// <summary>
    /// 失败操作的错误码，表示执行工作的 Grain 未确认该工作，因而无法确定工作是否已经运行：
    /// 启动消息在未得到确认时停止投递，或者在 Grain 的准入窗口结束后才到达，此时
    /// Grain 可能已运行并删除该工作。客户端应在重新启动工作前检查其实际效果（如工作创建的
    /// 资源），从而避免重复执行副作用。
    /// </summary>
    public const string OutcomeUnknownCode = "OutcomeUnknown";
}

/// <summary>表示其宿主未能在执行工作的 Grain 上启动工作的操作。</summary>
/// <remarks>
/// 调度操作的工作失败时，操作宿主以此异常失败，使客户端能够区分以下情况：工作
/// 从未运行（<see cref="DurableOperationError.NotStartedCode"/>）、工作结果未知
/// （<see cref="DurableOperationError.OutcomeUnknownCode"/>），以及工作已经运行但失败。
/// </remarks>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableOperationStartException")]
internal sealed class DurableOperationStartException : Exception
{
    private DurableOperationStartException()
    {
        ErrorCode = DurableOperationError.OutcomeUnknownCode;
    }

    private DurableOperationStartException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// 获取操作的错误码：<see cref="DurableOperationError.NotStartedCode"/> 或
    /// <see cref="DurableOperationError.OutcomeUnknownCode"/>.
    /// </summary>
    [Id(0)]
    public string ErrorCode { get; private set; }

    /// <summary>
    /// 返回用于报告 <paramref name="failure"/>（操作工作调度失败）的异常；若属于
    /// 其他失败，则返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 只有执行工作的 Grain 在未存储工作时作出的拒绝（即冲突或带有自身错误码的拒绝），
    /// 才能证明工作从未运行。投递在未获确认时停止，或调用在
    /// Grain 的准入窗口结束后到达，均意味着结果未知：Grain 可能已运行甚至已删除该工作。
    /// </remarks>
    public static DurableOperationStartException? FromScheduleFailure(Exception failure) => failure switch
    {
        DurableTaskConflictException => NotStarted(failure),
        DurableTaskRejectedException { Code: DurableTaskRejectionCodes.Expired } => OutcomeUnknown(failure),
        DurableTaskRejectedException => NotStarted(failure),
        DurableTaskDeliveryException => OutcomeUnknown(failure),
        _ => null,
    };

    private static DurableOperationStartException NotStarted(Exception failure) =>
        new(DurableOperationError.NotStartedCode, "The operation's work wasn't started.", failure);

    private static DurableOperationStartException OutcomeUnknown(Exception failure) =>
        new(DurableOperationError.OutcomeUnknownCode, "The operation's work wasn't confirmed, so whether it ran is unknown.", failure);
}
/// <summary>为所有提供持久化任务服务的协议将任务响应描述为操作。</summary>
internal static class DurableOperationResponses
{
    /// <summary>返回用于向客户端说明失败或取消响应的错误。</summary>
    public static DurableOperationError GetError(DurableTaskResponse response, DurableHttpOptions options) => response.Exception switch
    {
        DurableOperationStartException start => new(start.ErrorCode, start.Message),
        { } exception => options.CreateError(exception),
        null => throw new InvalidOperationException($"A {response.Status} response carries its exception."),
    };

    /// <summary>返回操作 <paramref name="operationId"/> 的状态监视器，其响应为 <paramref name="response"/>。</summary>
    public static DurableOperation ToOperation(string operationId, DurableTaskResponse response, JsonSerializerOptions json, DurableHttpOptions options) =>
        response.Status switch
        {
            DurableTaskStatus.CompletedSuccessfully => new(
                operationId,
                DurableOperationStatus.Succeeded,
                Result: response.ResultType is { } resultType ? JsonSerializer.SerializeToElement(response.Result, json.GetTypeInfo(resultType)) : null),
            DurableTaskStatus.Failed => new(operationId, DurableOperationStatus.Failed, Error: GetError(response, options)),
            DurableTaskStatus.Canceled => new(operationId, DurableOperationStatus.Canceled, Error: GetError(response, options)),
            _ => new(operationId, DurableOperationStatus.Running),
        };

    /// <summary>返回终态响应对应的 Nexus 状态。</summary>
    public static string GetNexusState(DurableTaskResponse response) => response.Status switch
    {
        DurableTaskStatus.CompletedSuccessfully => NexusProtocol.Succeeded,
        DurableTaskStatus.Failed => NexusProtocol.Failed,
        DurableTaskStatus.Canceled => NexusProtocol.Canceled,
        var status => throw new InvalidOperationException($"A Nexus state describes a terminal response, not a {status} one."),
    };

    /// <summary>返回用于报告失败或取消响应的 Nexus <c>Failure</c>。</summary>
    public static NexusFailure ToNexusFailure(DurableTaskResponse response, DurableHttpOptions options)
    {
        var error = GetError(response, options);
        return new(
            error.Message,
            new() { ["type"] = NexusProtocol.OperationErrorType },
            new() { ["state"] = GetNexusState(response), ["code"] = error.Code });
    }
}

/// <summary>请求取消操作。</summary>
/// <param name="Reason">
/// 被取消的任务从 <see cref="DurableTaskContext.TaskCancellationReason"/> 读取的原因；
/// 无原因时为 <see langword="null"/>。
/// </param>
public sealed record DurableOperationCancelRequest(
    [property: JsonPropertyName("reason")] string? Reason);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DurableOperation))]
[JsonSerializable(typeof(DurableOperationCancelRequest))]
internal sealed partial class DurableHttpJsonContext : JsonSerializerContext;

internal static class DurableHttpJsonExtensions
{
    /// <summary>
    /// 返回 <typeparamref name="T"/> 的契约，该契约由 <paramref name="options"/> 解析，因此序列化使用
    /// 选项的解析器（例如应用的源代码生成上下文），而不是反射。
    /// </summary>
    public static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
