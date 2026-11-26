using System.Text.Json.Serialization;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 此 grain发件箱的引用点仍然保留着,运行时日记直到发件箱退掉。
/// </summary>
/// <param name="Target">援引开始任务时的 grain 。</param>
/// <param name="Payload">编码的调用信。</param>
/// <param name="RecordId">
/// 记录起始时间及其结果的任务的线性识别符号:电话本身的任务,或独立任务
/// 订阅记录。 一个普通 grain 方法所张贴的根任务没有根任务。
/// 记录树的运行,它被删除了,没有改变任何东西。
/// </param>
/// <remarks>
/// 等待援引的表格与处决树分开,因此,在删除该表之后,仍可继续适用。
/// 树的开始,
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Tasks.Runtime.PendingInvocation")]
internal sealed record PendingInvocation(
    [property: Id(0)] GrainId Target,
    [property: Id(1)] byte[] Payload,
    [property: Id(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    TaskId? RecordId);
