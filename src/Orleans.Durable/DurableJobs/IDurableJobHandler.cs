using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 提供关于持久工作执行的背景资料。
/// </summary>
public interface IJobRunContext
{
    /// <summary>
    /// 执行持久工作。
    /// </summary>
    DurableJob Job { get; }

    /// <summary>
    /// 获取此执行运行的唯一身份标识符。每次任务被卸排时都会产生新的身份标识符
    /// 并且不保存于每条线或散装调任之间。
    /// </summary>
    string RunId { get; }

    /// <summary>
    /// 获得这项工作被卸排执行的次数,包括重试。
    /// </summary>
    int DequeueCount { get; }
}

/// <summary>
/// 代表持久工作的执行背景。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Jobs.JobRunContext")]
internal class JobRunContext : IJobRunContext
{
    /// <summary>
    /// 执行持久工作。
    /// </summary>
    [Id(0)]
    public DurableJob Job { get; }

    /// <summary>
    /// 获取此执行运行的唯一身份标识符。每次任务被卸排时都会产生新的身份标识符
    /// 并且不保存于每条线或散装调任之间。
    /// </summary>
    [Id(1)]
    public string RunId { get; }

    /// <summary>
    /// 获得这项工作被卸排执行的次数,包括重试。
    /// </summary>
    [Id(2)]
    public int DequeueCount { get; }

    /// <summary>
    /// 初始化 <see cref="JobRunContext"/> 类的新实例。
    /// </summary>
    /// <param name="job">持久执行的工作。</param>
    /// <param name="runId">此执行运行的唯一标识符。</param>
    /// <param name="retryCount">这项工作被取消排队的次数,包括重试次数。</param>
    public JobRunContext(DurableJob job, string runId, int retryCount)
    {
        Job = job;
        RunId = runId;
        DequeueCount = retryCount;
    }
}

/// <summary>
/// 定义处理持久任务执行的界面。
/// Grains 实施此接口以接收和处理持久工作。
/// </summary>
/// <remarks>
/// <para>
/// 实施此接口的Grains 可针对长期工作。
/// 当工作达到规定时间时,即援用 <see cref="ExecuteJobAsync"/> 方法。
/// </para>
/// <example>
/// 以下示例表明 grain 执行 <see cref="IDurableJobHandler"/>:
/// <code>
/// public class MyGrain : Grain, IDurableJobHandler
/// {
///     public Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
///     {
///         // 处理持久作业
///         var jobName = context.Job.Name;
///         var dueTime = context.Job.DueTime;
///
///         // 此令牌用于请求取消本次执行，例如在 Silo 关闭时。
///         // 持久作业仍可在另一台主机上重试。
///         attemptCancellationToken.ThrowIfCancellationRequested();
///
///         // 在此执行作业逻辑
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
    /// 以所提供的背景执行持久工作。
    /// </summary>
    /// <param name="context">包含持久工作执行信息的背景。</param>
    /// <param name="attemptCancellationToken">
    /// 合作要求取消目前执行企图的象征。
    /// 取消企图并不取消持久工作本身;该工作仍有资格再次尝试。
    /// </param>
    /// <returns>代表着无休止的 工作执行操作的任务</returns>
    /// <remarks>
    /// <para>
    /// 使用这种方法的 Orleans 工作达到适当时间后,可持久就业基础设施。
    /// 执行应处理工作执行逻辑,并可以使用来自 <paramref name="context"/>
    /// 获取工作元数据、删除重试逻辑的计数和其他执行细节。
    /// </para>
    /// <para>
    /// 如果该方法丢弃了例外, 并配置重试策略, 可以重新尝试该工作 。
    /// <see cref="IJobRunContext.DequeueCount"/> 属性可用于确定是否尝试重试。
    /// </para>
    /// <para>
    /// 要求通过 <see cref="ILocalDurableJobManager.CancelAsync"/>持久取消工作。
    /// 这项请求防止了今后企图,但并未强行制止已经进行中的企图。
    /// </para>
    /// </remarks>
    Task ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken);
}
