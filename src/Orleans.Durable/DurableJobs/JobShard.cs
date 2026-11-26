using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 代表着在特定时限内管理大量工作的持久工作。
/// 工作碎片负责储存、回收和管理持久工作的生命周期
/// 属于指定时间窗口。
/// </summary>
/// <remarks>
/// 使用工作碎片来将可持久工作分成不同时间段,以提高可缩放性
/// 和性能。每个碎片都有明确的开始时间和结束时间,确定哪些任务
/// 。当所有工作都在时间范围内时, 碎片可以标记为完整
/// 已经处理完毕。
/// </remarks>
public interface IJobShard : IAsyncDisposable
{
    /// <summary>
    /// 获取此工作碎片的独特标识符 。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 获取由此碎片管理的时段的起始时间 。
    /// </summary>
    DateTimeOffset StartTime { get; }

    /// <summary>
    /// 获取由此碎片管理的时间范围结束时间 。
    /// </summary>
    DateTimeOffset EndTime { get; }

    /// <summary>
    /// 获取与此工作碎片相关的可选元数据。
    /// </summary>
    IDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// 获得一个值, 显示是否该碎片被标记为完整且不再接受新任务 。
    /// </summary>
    /// <remarks>
    /// 当一个碎片被标记成完整的时, <see cref="MarkAsCompleteAsync"/>),不能增加新的工作。
    /// </remarks>
    bool IsAddingCompleted { get; }

    /// <summary>
    /// 以合适的时间为条件,
    /// </summary>
    /// <returns>大量持久的工作环境。</returns>
    IAsyncEnumerable<IJobRunContext> ConsumeDurableJobsAsync();

    /// <summary>
    /// 试图保留一个被解密的持久工作供执行。
    /// </summary>
    /// <param name="jobContext">被裁员的工作环境</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>
    /// 包含任务 <see cref="DurableJobMutationResult.Applied"/> , 当尝试开始时
    /// <see cref="DurableJobMutationResult.JobNotFound"/> 取消或完成该工作后,首先将职位从岗位上撤走,
    /// 或 <see cref="DurableJobMutationResult.OwnershipLost"/> ,当此碎片不再由本地所有时。
    /// </returns>
    Task<DurableJobMutationResult> TryStartAttemptAsync(
        IJobRunContext jobContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取当前在此碎片中安排的工作数量 。
    /// </summary>
    /// <returns>代表无同步操作的任务。任务结果包含任务计数。</returns>
    ValueTask<int> GetJobCountAsync();

    /// <summary>
    /// 将这一碎片标为完整, 从而阻止新任务排期 。
    /// </summary>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>代表无同步操作的任务。</returns>
    Task MarkAsCompleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 从这个碎片中删除一个耐久的工作 。
    /// </summary>
    /// <param name="jobId">要删除的任务的唯一标识符。</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>一项任务包含持久的突变结果。</returns>
    Task<DurableJobMutationResult> RemoveJobAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 重新安排日后再找工作。
    /// </summary>
    /// <param name="jobContext">要重新尝试的工作的上下文。</param>
    /// <param name="newDueTime">
    /// 任务的新到期时间。 此值取代 <see cref="DurableJob.DueTime"/>
    /// <see cref="IJobRunContext.Job"/>.
    /// </param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>一项任务包含持久的突变结果。</returns>
    Task<DurableJobMutationResult> RetryJobLaterAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// 重新安排成功完成的执行, 并重置计数。
    /// </summary>
    /// <param name="jobContext">已完成执行的背景。</param>
    /// <param name="newDueTime">
    /// 任务的新到期时间。 此值取代 <see cref="DurableJob.DueTime"/>
    /// <see cref="IJobRunContext.Job"/>.
    /// </param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>一项任务包含持久的突变结果。</returns>
    Task<DurableJobMutationResult> RescheduleJobAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// 试图在这个碎片上安排新工作
    /// </summary>
    /// <param name="request">载有工作时间安排参数的请求。</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>代表非同步操作的任务。 任务结果包含如果成功, 则持久工作; 如果无法安排该工作, 则无效(e.g. 碎片标记为完整) 。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当到期时间超出碎片的时间范围时丢弃。</exception>
    Task<DurableJob?> TryScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 描述执行后试图改变持久工作的结果。
/// </summary>
public enum DurableJobMutationResult
{
    /// <summary>
    /// 变异被长期应用
    /// </summary>
    Applied,

    /// <summary>
    /// 这项工作已经不存在,通常是由于取消或完成后首先取消了。
    /// </summary>
    JobNotFound,

    /// <summary>
    /// 本地 silo 不再拥有碎片, 因此突变没有应用 。
    /// </summary>
    OwnershipLost
}

/// <summary>
/// 实施 <see cref="IJobShard"/> 的基础,为工作零散化实施提供共同功能。
/// </summary>
public abstract class JobShard : IJobShard
{
    private readonly InMemoryJobQueue _jobQueue;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    /// <inheritdoc/>
    public string Id { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset StartTime { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset EndTime { get; protected set; }

    /// <inheritdoc/>
    public IDictionary<string, string>? Metadata { get; protected set; }

    /// <inheritdoc/>
    public bool IsAddingCompleted { get; protected set; }

    /// <summary>
    /// 初始化 <see cref="JobShard"/> 类的新实例。
    /// </summary>
    /// <param name="id">此工作碎片的独特标识符。</param>
    /// <param name="startTime">由此碎片管理的时段的起始时间。</param>
    /// <param name="endTime">由此碎片管理的时间范围的结束时间。</param>
    protected JobShard(string id, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        Id = id;
        StartTime = startTime;
        EndTime = endTime;
        _jobQueue = new InMemoryJobQueue();
    }

    /// <inheritdoc/>
    public ValueTask<int> GetJobCountAsync() => ValueTask.FromResult(_jobQueue.Count);

    /// <inheritdoc/>
    public IAsyncEnumerable<IJobRunContext> ConsumeDurableJobsAsync()
    {
        return _jobQueue;
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> TryStartAttemptAsync(
        IJobRunContext jobContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            return _jobQueue.ContainsJob(jobContext.Job.Id)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<DurableJob?> TryScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (IsAddingCompleted)
            {
                return null;
            }

            if (request.DueTime < StartTime || request.DueTime > EndTime)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Scheduled time is out of shard bounds.");
            }

            var jobId = Guid.NewGuid().ToString();
            var job = new DurableJob
            {
                Id = jobId,
                TargetGrainId = request.Target,
                Name = request.JobName,
                DueTime = request.DueTime,
                ShardId = Id,
                Metadata = request.Metadata,
                TraceParent = request.TraceParent,
                TraceState = request.TraceState,
            };

            await PersistAddJobAsync(job, cancellationToken);
            _jobQueue.Enqueue(job, 0);
            return job;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> RemoveJobAsync(string jobId, CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!_jobQueue.ContainsJob(jobId))
            {
                return DurableJobMutationResult.JobNotFound;
            }

            await PersistRemoveJobAsync(jobId, cancellationToken);
            return _jobQueue.RemoveJob(jobId)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task MarkAsCompleteAsync(CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            IsAddingCompleted = true;
            _jobQueue.MarkAsComplete();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> RetryJobLaterAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!_jobQueue.ContainsJob(jobContext.Job.Id))
            {
                return DurableJobMutationResult.JobNotFound;
            }

            await PersistRetryJobAsync(jobContext, newDueTime, cancellationToken);
            return _jobQueue.RetryJobLater(jobContext.Job.Id, newDueTime, jobContext.DequeueCount)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<DurableJobMutationResult> RescheduleJobAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!_jobQueue.ContainsJob(jobContext.Job.Id))
            {
                return DurableJobMutationResult.JobNotFound;
            }

            var executionGeneration = checked(jobContext.Job.ExecutionGeneration + 1);
            var resetContext = new JobRunContext(
                jobContext.Job.WithExecutionGeneration(executionGeneration),
                jobContext.RunId,
                retryCount: 0);
            await PersistRetryJobAsync(resetContext, newDueTime, cancellationToken);
            return _jobQueue.RetryJobLater(jobContext.Job.Id, newDueTime, dequeueCount: 0, executionGeneration)
                ? DurableJobMutationResult.Applied
                : DurableJobMutationResult.JobNotFound;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <summary>
    /// 将一项任务加入含有指定删除数的模拟队列。
    /// </summary>
    /// <param name="job">任务要排队。</param>
    /// <param name="dequeueCount">任务被排队的次数。</param>
    protected void EnqueueJob(DurableJob job, int dequeueCount)
    {
        _jobQueue.Enqueue(job, dequeueCount);
    }

    /// <summary>
    /// 坚持在仓库里增加一份新工作。
    /// </summary>
    /// <param name="job">这份工作要坚持下去</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>代表无同步操作的任务。</returns>
    protected abstract Task PersistAddJobAsync(DurableJob job, CancellationToken cancellationToken);

    /// <summary>
    /// 坚持要从底库仓库中撤换一份工作。
    /// </summary>
    /// <param name="jobId">要删除的任务的唯一标识符。</param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>代表无同步操作的任务。</returns>
    protected abstract Task PersistRemoveJobAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 坚持将工作重新安排到仓库
    /// </summary>
    /// <param name="jobContext">要重试的任务执行环境。</param>
    /// <param name="newDueTime">
    /// 任务的新到期时间。 此值取代 <see cref="DurableJob.DueTime"/>
    /// <see cref="IJobRunContext.Job"/>.
    /// </param>
    /// <param name="cancellationToken">取消操作的标记 。</param>
    /// <returns>代表无同步操作的任务。</returns>
    protected abstract Task PersistRetryJobAsync(IJobRunContext jobContext, DateTimeOffset newDueTime, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}
