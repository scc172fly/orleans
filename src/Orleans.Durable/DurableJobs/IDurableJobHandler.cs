using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 提供持久化作业执行的上下文信息。
/// </summary>
public interface IJobRunContext
{
    /// <summary>
    /// 获取正在执行的持久化作业。
    /// </summary>
    DurableJob Job { get; }

    /// <summary>
    /// 获取本次执行实例的唯一标识符。每次作业出队时均生成新标识符，
    /// 不会在重试或分片重新分配时保留。
    /// </summary>
    string RunId { get; }

    /// <summary>
    /// 获取此作业为执行而出队的次数，包括重试。
    /// </summary>
    int DequeueCount { get; }
}

/// <summary>
/// 表示持久化作业的执行上下文。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Jobs.JobRunContext")]
internal class JobRunContext : IJobRunContext
{
    /// <summary>
    /// 获取正在执行的持久化作业。
    /// </summary>
    [Id(0)]
    public DurableJob Job { get; }

    /// <summary>
    /// 获取本次执行实例的唯一标识符。每次作业出队时均生成新标识符，
    /// 不会在重试或分片重新分配时保留。
    /// </summary>
    [Id(1)]
    public string RunId { get; }

    /// <summary>
    /// 获取此作业为执行而出队的次数，包括重试。
    /// </summary>
    [Id(2)]
    public int DequeueCount { get; }

    /// <summary>
    /// 初始化 <see cref="JobRunContext"/> 类的新实例。
    /// </summary>
    /// <param name="job">要执行的持久化作业。</param>
    /// <param name="runId">本次执行实例的唯一标识符。</param>
    /// <param name="retryCount">此作业的出队次数，包括重试。</param>
    public JobRunContext(DurableJob job, string runId, int retryCount)
    {
        Job = job;
        RunId = runId;
        DequeueCount = retryCount;
    }
}

/// <summary>
/// 定义处理持久化作业执行的接口。
/// Grain 实现此接口以接收并处理持久化作业。
/// </summary>
/// <remarks>
/// <para>
/// 实现此接口的 Grain 可以成为持久化作业的目标。
/// 到达作业到期时间时调用 <see cref="ExecuteJobAsync"/> 方法。
/// </para>
/// <example>
/// 以下示例展示实现 <see cref="IDurableJobHandler"/> 的 Grain：
/// <code>
/// public class MyGrain : Grain, IDurableJobHandler
/// {
///     public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
///     {
///         // Process the durable job
///         var jobName = context.Job.Name;
///         var dueTime = context.Job.DueTime;
///
///         // This token requests cancellation of this execution attempt, for example during silo shutdown.
///         // The durable job remains eligible for another attempt on another host.
///         attemptCancellationToken.ThrowIfCancellationRequested();
///
///         // Perform job logic here
///
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public interface IDurableJobHandler
{
    /// <summary>
    /// 使用提供的上下文执行持久化作业。
    /// </summary>
    /// <param name="context">包含持久化作业执行信息的上下文。</param>
    /// <param name="attemptCancellationToken">
    /// 用于协作式请求取消当前执行尝试的令牌。
    /// 取消执行尝试并不取消持久化作业本身；该作业仍可再次尝试执行。
    /// </param>
    /// <returns>表示异步作业执行操作的任务。</returns>
    /// <remarks>
    /// <para>
    /// 到达作业到期时间时，Orleans 持久化作业基础设施调用此方法。
    /// 实现应负责处理作业执行逻辑，并可利用 <paramref name="context"/> 中的信息
    /// 访问作业元数据、用于重试逻辑的出队计数及其他执行细节。
    /// </para>
    /// <para>
    /// 如果方法抛出异常且已配置重试策略，则可能重试作业。
    /// 可使用 <see cref="IJobRunContext.DequeueCount"/> 属性判断本次是否为重试尝试。
    /// </para>
    /// <para>
    /// 通过 <see cref="ILocalDurableJobManager.CancelAsync"/> 请求取消持久化作业。
    /// 此请求阻止将来的执行尝试，但不会强制停止已在运行的尝试。
    /// </para>
    /// </remarks>
    Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken);
}
