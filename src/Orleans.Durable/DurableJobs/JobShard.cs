using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 表示管理指定时间范围内一组作业的持久化作业分片。
/// 作业分片负责存储、检索和管理
/// 处于其指定时间窗口内的持久化作业的生命周期。
/// </summary>
/// <remarks>
/// 作业分片按时间范围对持久化作业进行分区，以提高可伸缩性
/// 和性能。每个分片具有明确的开始和结束时间，用于确定其管理的作业。
/// 当分片时间范围内的所有作业
/// 均已得到处理后，可将分片标记为完成。
/// </remarks>
public interface IJobShard : IAsyncDisposable
{
    /// <summary>
    /// 获取此作业分片的唯一标识符。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 获取此分片管理的时间范围的开始时间。
    /// </summary>
    DateTimeOffset StartTime { get; }

    /// <summary>
    /// 获取此分片管理的时间范围的结束时间。
    /// </summary>
    DateTimeOffset EndTime { get; }

    /// <summary>
    /// 获取与此作业分片关联的可选元数据。
    /// </summary>
    IDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// 获取一个指示分片是否已标记为完成且不再接受新作业的值。
    /// </summary>
    /// <remarks>
    /// 通过 <see cref="MarkAsCompleteAsync"/> 将分片标记为完成后，无法再向其中添加新作业。
    /// </remarks>
    bool IsAddingCompleted { get; }

    /// <summary>
    /// 按到期时间顺序消费此分片中的持久化作业。
    /// </summary>
    /// <returns>持久化作业上下文的异步可枚举序列。</returns>
    IAsyncEnumerable<IJobRunContext> ConsumeDurableJobsAsync();

    /// <summary>
    /// 尝试为已出队的持久化作业预留执行资格。
    /// </summary>
    /// <param name="jobContext">已出队作业的上下文。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>
    /// 返回一个任务：允许启动执行尝试时，其结果为 <see cref="DurableJobMutationResult.Applied"/>；
    /// 取消或完成操作已先行移除作业时，结果为 <see cref="DurableJobMutationResult.JobNotFound"/>；
    /// 本地 Silo 不再拥有此分片时，结果为 <see cref="DurableJobMutationResult.OwnershipLost"/>。
    /// </returns>
    Task<DurableJobMutationResult> TryStartAttemptAsync(
        IJobRunContext jobContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取此分片中当前已调度的作业数量。
    /// </summary>
    /// <returns>表示异步操作的任务，其结果包含作业数量。</returns>
    ValueTask<int> GetJobCountAsync();

    /// <summary>
    /// 将此分片标记为完成，阻止调度新作业。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task MarkAsCompleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 从此分片中移除持久化作业。
    /// </summary>
    /// <param name="jobId">要移除的作业的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>包含持久化变更结果的任务。</returns>
    Task<DurableJobMutationResult> RemoveJobAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 将作业重新调度到较晚的时间以供重试。
    /// </summary>
    /// <param name="jobContext">要重试的作业的上下文。</param>
    /// <param name="newDueTime">
    /// 作业的新到期时间。此值取代 <see cref="DurableJob.DueTime"/>，该属性属于
    /// <see cref="IJobRunContext.Job"/>。
    /// </param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>包含持久化变更结果的任务。</returns>
    Task<DurableJobMutationResult> RetryJobLaterAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// 对成功完成的执行进行重新调度，并重置其出队计数。
    /// </summary>
    /// <param name="jobContext">已完成的执行的上下文。</param>
    /// <param name="newDueTime">
    /// 作业的新到期时间。此值取代 <see cref="DurableJob.DueTime"/>，该属性属于
    /// <see cref="IJobRunContext.Job"/>。
    /// </param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>包含持久化变更结果的任务。</returns>
    Task<DurableJobMutationResult> RescheduleJobAsync(
        IJobRunContext jobContext,
        DateTimeOffset newDueTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// 尝试在此分片上调度新作业。
    /// </summary>
    /// <param name="request">包含作业调度参数的请求。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示异步操作的任务。成功时，其结果包含持久化作业；无法调度作业时（例如分片已标记为完成），结果为 null。</returns>
    /// <exception cref="ArgumentOutOfRangeException">到期时间超出分片的时间范围时抛出。</exception>
    Task<DurableJob?> TryScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 描述作业执行后尝试对持久化作业进行变更的结果。
/// </summary>
public enum DurableJobMutationResult
{
    /// <summary>
    /// 变更已持久化应用。
    /// </summary>
    Applied,

    /// <summary>
    /// 作业已不存在，通常是因为取消或完成操作已先行将其移除。
    /// </summary>
    JobNotFound,

    /// <summary>
    /// 本地 Silo 已不再拥有此分片，因此未应用该变更。
    /// </summary>
    OwnershipLost
}

/// <summary>
/// <see cref="IJobShard"/> 的基础实现，为作业分片的各种实现提供通用功能。
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
    /// <param name="id">此作业分片的唯一标识符。</param>
    /// <param name="startTime">此分片管理的时间范围的开始时间。</param>
    /// <param name="endTime">此分片管理的时间范围的结束时间。</param>
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
    /// 使用指定的出队计数将作业加入内存队列。
    /// </summary>
    /// <param name="job">要入队的作业。</param>
    /// <param name="dequeueCount">此作业的出队次数。</param>
    protected void EnqueueJob(DurableJob job, int dequeueCount)
    {
        _jobQueue.Enqueue(job, dequeueCount);
    }

    /// <summary>
    /// 将添加新作业的操作持久化到底层存储。
    /// </summary>
    /// <param name="job">要持久化的作业。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected abstract Task PersistAddJobAsync(DurableJob job, CancellationToken cancellationToken);

    /// <summary>
    /// 将移除作业的操作持久化到底层存储。
    /// </summary>
    /// <param name="jobId">要移除的作业的唯一标识符。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected abstract Task PersistRemoveJobAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 将重新调度作业的操作持久化到底层存储。
    /// </summary>
    /// <param name="jobContext">要重试的作业的执行上下文。</param>
    /// <param name="newDueTime">
    /// 作业的新到期时间。此值取代 <see cref="DurableJob.DueTime"/>，该属性属于
    /// <see cref="IJobRunContext.Job"/>。
    /// </param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected abstract Task PersistRetryJobAsync(IJobRunContext jobContext, DateTimeOffset newDueTime, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }
}
