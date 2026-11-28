using System.Text.Json.Serialization;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 本 Grain 的发件箱通过引用保存的调用，运行时将其记入日志，直到发件箱将其退役。
/// </summary>
/// <param name="Target">调用所启动任务所在的 Grain。</param>
/// <param name="Payload">编码后的调用消息。</param>
/// <param name="RecordId">
/// 记录启动操作及其结果的任务的传输标识符：可以是调用自身的任务，也可以是分离任务的
/// 订阅记录。由普通 Grain 方法投递的根任务没有此标识符。若退役通知指定记录所属执行树先前已删除的
/// 一次运行，则不产生任何变更。
/// </param>
/// <remarks>
/// 待处理调用表独立于执行树，因此即使删除了
/// 启动该调用的执行树，调用仍然保留。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Tasks.Runtime.PendingInvocation")]
internal sealed record PendingInvocation(
    [property: Id(0)] GrainId Target,
    [property: Id(1)] byte[] Payload,
    [property: Id(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    TaskId? RecordId);
