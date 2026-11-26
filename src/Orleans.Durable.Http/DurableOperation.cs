using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Orleans.Durable.Tasks;

namespace Orleans.Durable.Http;

/// <summary>
/// 代表行动现况监测器:报告持久任务状况的资源,一旦报告,则报告
/// 终端,其结果或错误。
/// </summary>
/// <param name="Id">处理状态监视器的操作识别符。</param>
/// <param name="Status">操作状态。</param>
/// <param name="Result">任务成功时, 当任务与一个值相继的结果。</param>
/// <param name="Error">任务失败或取消时,当任务失败或被取消时,任务失败或取消。</param>
/// <remarks>
/// 文件具有 Azure 长期运行状态监测器形状, 客户可以进行 Azure 调查业务
/// 长期的任务记录为根据 <see cref="IDurableHttpClient"/> 步骤的结果。
/// </remarks>
[Alias("Orleans.Durable.Http.DurableOperation")]
public sealed record DurableOperation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] DurableOperationStatus Status,
    [property: JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Result = null,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DurableOperationError? Error = null)
{
    /// <summary>获取操作是否达到终端状态。</summary>
    [JsonIgnore]
    public bool IsTerminal => Status is DurableOperationStatus.Succeeded or DurableOperationStatus.Failed or DurableOperationStatus.Canceled;

    /// <summary>
    /// 读取服务器返回的状态监视器 Azure 客户客户做: a <c>status</c> 的状态,该软件包的状态
    /// 名字不叫,比如 Azure 资源经理 <c>InProgress</c> 或 <c>Accepted</c>,读为
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

/// <summary>序列化a <see cref="DurableOperation"/> 用于 Orleans,其结果 JSON 文本。</summary>
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

/// <summary>确定行动状态。</summary>
/// <remarks>值为 Azure长期运行状态,按名称顺序排列。</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DurableOperationStatus>))]
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableOperationStatus")]
public enum DurableOperationStatus
{
    /// <summary>操作尚未启动。</summary>
    NotStarted,

    /// <summary>操作正在运行。 持久任务报告此状态, 直到它结束为止。</summary>
    Running,

    /// <summary>操作成功完成。</summary>
    Succeeded,

    /// <summary>操作失败。</summary>
    Failed,

    /// <summary>操作被取消。</summary>
    Canceled,
}

/// <summary>描述操作失败或取消的原因。</summary>
/// <param name="Code">一个机器可读代码,它 <see cref="DurableHttpOptions.CreateError"/> 选择。</param>
/// <param name="Message">人类可以读取的描述</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableOperationError")]
public sealed record DurableOperationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// 工作从未运行的失败操作代码, 因为运行该操作的 grain 拒绝不存储
    /// ,例如当工作标识符已经指定了其它方法的任务时。一个客户再次开始工作
    /// 具有 <c>Idempotency-Key</c>能力钥匙。
    /// </summary>
    public const string NotStartedCode = "NotStarted";

    /// <summary>
    /// 工作 grain 无法确认工程的失败操作代码, 因此是否运行未知:
    /// 未经确认而退休或抵达 grain入场窗口后,即
    /// 时间 grain 可能运行并删除它的时间。客户端检查工作效果, 如资源等
    /// 创造,在它重新开始工作之前, 这样工程的副作用就不会重复。
    /// </summary>
    public const string OutcomeUnknownCode = "OutcomeUnknown";
}

/// <summary>代表一项业务,其东道主未能开始有关该工程的工作 grain.</summary>
/// <remarks>
/// 操作的主机在工作时间安排失败时,除此例外之外,操作的主机失效,因此客户区分了哪些工作
/// 从未逃跑过 <see cref="DurableOperationError.NotStartedCode"/>和工作,其结果不明,
/// <see cref="DurableOperationError.OutcomeUnknownCode"/>,运行失败的工作。
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
    /// 获取操作错误代码 <see cref="DurableOperationError.NotStartedCode"/> 或
    /// <see cref="DurableOperationError.OutcomeUnknownCode"/>.
    /// </summary>
    [Id(0)]
    public string ErrorCode { get; private set; }

    /// <summary>
    /// 返回报告 <paramref name="failure"/>、未安排操作工作的例外,或
    /// 又一次失败<see langword="null"/>
    /// </summary>
    /// <remarks>
    /// 只有在工作 grain 拒绝时,不储存工作、冲突或拒绝使用自己的代码,
    /// 工作从未运行。 一种未确认的退货,或是在合同到期后收到的召回。
    /// grain的入场窗口,结果不明: grain 可能已经运行工作,甚至删除。
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
/// <summary>将持久任务应对办法描述为行动,适用于为其服务的每一项议定书。</summary>
internal static class DurableOperationResponses
{
    /// <summary>返回描述对客户端的失败或取消回复的错误。</summary>
    public static DurableOperationError GetError(DurableTaskResponse response, DurableHttpOptions options) => response.Exception switch
    {
        DurableOperationStartException start => new(start.ErrorCode, start.Message),
        { } exception => options.CreateError(exception),
        null => throw new InvalidOperationException($"A {response.Status} response carries its exception."),
    };

    /// <summary>返回运行状态监测器 <paramref name="operationId"/>,其答复是 <paramref name="response"/>。</summary>
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

    /// <summary>返回终端响应的 Nexus 状态。</summary>
    public static string GetNexusState(DurableTaskResponse response) => response.Status switch
    {
        DurableTaskStatus.CompletedSuccessfully => NexusProtocol.Succeeded,
        DurableTaskStatus.Failed => NexusProtocol.Failed,
        DurableTaskStatus.Canceled => NexusProtocol.Canceled,
        var status => throw new InvalidOperationException($"A Nexus state describes a terminal response, not a {status} one."),
    };

    /// <summary>返回报告答复失败或取消的Nexus <c>Failure</c> 。</summary>
    public static NexusFailure ToNexusFailure(DurableTaskResponse response, DurableHttpOptions options)
    {
        var error = GetError(response, options);
        return new(
            error.Message,
            new() { ["type"] = NexusProtocol.OperationErrorType },
            new() { ["state"] = GetNexusState(response), ["code"] = error.Code });
    }
}

/// <summary>要求取消一项行动。</summary>
/// <param name="Reason">
/// 取消任务的原因来自 <see cref="DurableTaskContext.TaskCancellationReason"/>, 或者
/// <see langword="null"/> 毫无理由。
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
    /// 返回合同 <typeparamref name="T"/> 减去 <paramref name="options"/> 解析, 所以序列化使用
    /// 选项的解析器, 如应用程序源生成上下文, 而不是反射 。
    /// </summary>
    public static JsonTypeInfo<T> GetTypeInfo<T>(this JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
