using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 提供按到期时间管理持久化作业的内存优先队列。
/// 作业按时间分桶，并在到期时异步枚举。
/// </summary>
internal sealed class InMemoryJobQueue : IAsyncEnumerable<IJobRunContext>
{
    private readonly TimeProvider _timeProvider;
    private readonly PriorityQueue<JobBucket, DateTimeOffset> _queue = new();
    private readonly Dictionary<string, JobBucket> _jobsIdToBucket = new();
    private readonly Dictionary<DateTimeOffset, JobBucket> _buckets = new();
    private TaskCompletionSource? _queueChangedWaiter;
    private int _jobCount;
    private bool _isComplete;
    private readonly Lock _syncLock = new();

    public InMemoryJobQueue(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 获取当前队列中的作业总数。
    /// </summary>
    public int Count => Volatile.Read(ref _jobCount);

    /// <summary>
    /// 使用指定的出队计数将持久化作业添加到队列中。
    /// </summary>
    /// <param name="job">要入队的持久化作业。</param>
    /// <param name="dequeueCount">此作业此前的出队次数。</param>
    /// <exception cref="InvalidOperationException">尝试向已标记完成的队列添加作业时抛出。</exception>
    /// <exception cref="ArgumentNullException">job 为 null 时抛出。</exception>
    public void Enqueue(DurableJob job, int dequeueCount)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (dequeueCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dequeueCount));
        }

        lock (_syncLock)
        {
            if (_isComplete)
                throw new InvalidOperationException("Cannot enqueue job to a completed queue.");

            var wakeCheckRequired = _queueChangedWaiter is not null;
            var previousNextDueTime = wakeCheckRequired ? GetNextDueTime() : null;
            var bucket = GetJobBucket(job.DueTime);
            var isReplacement = _jobsIdToBucket.TryGetValue(job.Id, out var existingBucket);
            if (existingBucket is not null && !ReferenceEquals(existingBucket, bucket))
            {
                existingBucket.RemoveJob(job.Id);
            }

            bucket.AddJob(job, dequeueCount);
            _jobsIdToBucket[job.Id] = bucket;
            if (!isReplacement)
            {
                Volatile.Write(ref _jobCount, _jobCount + 1);
            }

            if (wakeCheckRequired)
            {
                SignalQueueChangedIfNextDueTimeChanged(previousNextDueTime);
            }
        }
    }

    /// <summary>
    /// 将队列标记为完成，阻止后续作业入队。
    /// 标记为完成后，队列将处理完剩余作业，然后终止枚举。
    /// </summary>
    public void MarkAsComplete()
    {
        lock (_syncLock)
        {
            _isComplete = true;
            SignalQueueChanged();
        }
    }

    /// <summary>
    /// 从队列中移除持久化作业。
    /// </summary>
    /// <param name="jobId">要移除的作业的唯一标识符。</param>
    /// <returns>找到并移除作业时为 true；未找到时为 false。</returns>
    /// <remarks>
    /// 作业所在的时间桶仍留在优先队列中，直到该桶得到处理，但作业本身会立即移除。
    /// </remarks>
    public bool RemoveJob(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        lock (_syncLock)
        {
            var wakeCheckRequired = _queueChangedWaiter is not null;
            var previousNextDueTime = wakeCheckRequired ? GetNextDueTime() : null;
            if (_jobsIdToBucket.TryGetValue(jobId, out var bucket))
            {
                // 尝试从时间桶中移除作业（可能已出队）。
                bucket.RemoveJob(jobId);
                _jobsIdToBucket.Remove(jobId);
                Volatile.Write(ref _jobCount, _jobCount - 1);
                // 注意：时间桶仍留在优先队列中，直到得到处理。
                if (wakeCheckRequired)
                {
                    SignalQueueChangedIfNextDueTimeChanged(previousNextDueTime);
                }

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 返回队列中是否仍包含指定的持久化作业。
    /// </summary>
    public bool ContainsJob(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        lock (_syncLock)
        {
            return _jobsIdToBucket.ContainsKey(jobId);
        }
    }

    /// <summary>
    /// 将作业重新调度至新的到期时间以供重试。
    /// </summary>
    /// <param name="jobContext">要重试的作业的上下文。</param>
    /// <param name="newDueTime">作业的新到期时间。</param>
    /// <remarks>
    /// 将作业从当前时间桶移除，并根据指定的到期时间添加到新时间桶中。
    /// 保留上下文中的出队计数。
    /// </remarks>
    public void RetryJobLater(IJobRunContext jobContext, DateTimeOffset newDueTime)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        _ = RetryJobLater(jobContext.Job.Id, newDueTime, jobContext.DequeueCount, executionGeneration: null);
    }

    /// <summary>
    /// 将作业重新调度至新的到期时间以供重试。
    /// </summary>
    /// <param name="jobId">要重试的作业的唯一标识符。</param>
    /// <param name="newDueTime">作业的新到期时间。</param>
    /// <param name="dequeueCount">与重试作业关联的已持久化出队计数。</param>
    /// <returns>找到作业并重新调度时为 true；未找到时为 false。</returns>
    public bool RetryJobLater(string jobId, DateTimeOffset newDueTime, int dequeueCount)
        => RetryJobLater(jobId, newDueTime, dequeueCount, executionGeneration: null);

    internal bool RetryJobLater(string jobId, DateTimeOffset newDueTime, int dequeueCount, long? executionGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (dequeueCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dequeueCount));
        }

        lock (_syncLock)
        {
            var wakeCheckRequired = _queueChangedWaiter is not null;
            var previousNextDueTime = wakeCheckRequired ? GetNextDueTime() : null;
            if (!_jobsIdToBucket.TryGetValue(jobId, out var oldBucket) || !oldBucket.TryGetJob(jobId, out var existing))
            {
                return false;
            }

            var newJob = new DurableJob
            {
                Id = existing.Job.Id,
                Name = existing.Job.Name,
                DueTime = newDueTime,
                TargetGrainId = existing.Job.TargetGrainId,
                ShardId = existing.Job.ShardId,
                Metadata = existing.Job.Metadata,
                TraceParent = existing.Job.TraceParent,
                TraceState = existing.Job.TraceState,
                ExecutionGeneration = executionGeneration ?? existing.Job.ExecutionGeneration,
            };

            oldBucket.RemoveJob(jobId);
            _jobsIdToBucket.Remove(jobId);
            var newBucket = GetJobBucket(newDueTime);
            newBucket.AddJob(newJob, dequeueCount);
            _jobsIdToBucket[jobId] = newBucket;
            if (wakeCheckRequired)
            {
                SignalQueueChangedIfNextDueTimeChanged(previousNextDueTime);
            }

            return true;
        }
    }

    /// <summary>
    /// 获取当前存续作业及其已持久化出队计数在某一时刻的快照。
    /// </summary>
    /// <returns>当前存续的作业及其出队计数。</returns>
    public IReadOnlyList<(DurableJob Job, int DequeueCount)> GetSnapshot()
    {
        lock (_syncLock)
        {
            var result = new List<(DurableJob Job, int DequeueCount)>(_jobsIdToBucket.Count);
            foreach (var (jobId, bucket) in _jobsIdToBucket)
            {
                if (bucket.TryGetJob(jobId, out var item))
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 清除所有队列状态。
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            var wakeCheckRequired = _queueChangedWaiter is not null;
            var previousNextDueTime = wakeCheckRequired ? GetNextDueTime() : null;
            _queue.Clear();
            _jobsIdToBucket.Clear();
            _buckets.Clear();
            Volatile.Write(ref _jobCount, 0);
            _isComplete = false;
            if (wakeCheckRequired)
            {
                SignalQueueChangedIfNextDueTimeChanged(previousNextDueTime);
            }
        }
    }

    /// <summary>
    /// 返回一个在持久化作业到期时产出作业的异步枚举器。
    /// </summary>
    /// <param name="cancellationToken">用于监视取消请求的令牌。</param>
    /// <returns>
    /// 一个为到期作业返回 <see cref="IJobRunContext"/> 实例的异步枚举器。
    /// 当队列发生变化或下一个作业到期时唤醒枚举器；当队列已标记完成且为空时终止枚举。
    /// </returns>
    public async IAsyncEnumerator<IJobRunContext> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            List<(DurableJob Job, int DequeueCount)>? jobsToYield = null;
            JobBucket? bucketBeingProcessed = null;
            Task? queueChanged = null;
            TimeSpan? delay = null;

            lock (_syncLock)
            {
                RemoveEmptyBuckets();

                if (Count == 0)
                {
                    if (_isComplete)
                    {
                        yield break; // 如果队列已冻结且为空，则退出。
                    }

                    queueChanged = GetQueueChangedTask();
                }
                else if (_queue.Count > 0)
                {
                    var nextBucket = _queue.Peek();
                    var now = _timeProvider.GetUtcNow();
                    if (nextBucket.DueTime <= now)
                    {
                        // 以原子方式将时间桶出队并从 _buckets 中移除，确保并发的
                        // Enqueue 调用不能复用相同 DueTime 的此时间桶。否则，
                        // GetJobBucket 会在 _buckets 中找到仍然存在的时间桶并向其中添加作业，
                        // 但时间桶已不在 _queue 中，新作业将无法得到处理。
                        var bucketToProcess = _queue.Dequeue();
                        bucketBeingProcessed = bucketToProcess;
                        _buckets.Remove(bucketToProcess.DueTime);

                        // 在锁保护下获取作业快照，避免并发的 Cancel/Retry 变更操作
                        // 与枚举发生竞争。
                        jobsToYield = new List<(DurableJob Job, int DequeueCount)>(bucketToProcess.Jobs);
                    }
                    else
                    {
                        queueChanged = GetQueueChangedTask();
                        delay = nextBucket.DueTime - now;
                    }
                }
                else
                {
                    queueChanged = GetQueueChangedTask();
                }
            }

            if (jobsToYield is not null)
            {
                // 在锁外处理时间桶中的所有作业，以提高并发能力。
                foreach (var (job, dequeueCount) in jobsToYield)
                {
                    // 确认作业未在处理期间被移除。
                    bool shouldYield;
                    lock (_syncLock)
                    {
                        shouldYield = bucketBeingProcessed is not null
                            && _jobsIdToBucket.TryGetValue(job.Id, out var currentBucket)
                            && ReferenceEquals(currentBucket, bucketBeingProcessed)
                            && bucketBeingProcessed.ContainsJob(job);
                        // 在 _jobsIdToBucket 中保留作业，以便通过 CancelJob/RetryJobLater 显式移除。
                    }

                    if (shouldYield)
                    {
                        yield return new JobRunContext(job, Guid.NewGuid().ToString(), dequeueCount + 1);
                    }
                }
            }
            else
            {
                await WaitForQueueChangeOrDelayAsync(queueChanged!, delay, cancellationToken);
            }
        }
    }

    private JobBucket GetJobBucket(DateTimeOffset dueTime)
    {
        if (!_buckets.TryGetValue(dueTime, out var bucket))
        {
            bucket = new JobBucket(dueTime);
            _buckets[dueTime] = bucket;
            _queue.Enqueue(bucket, dueTime);
        }

        return bucket;
    }

    private void RemoveEmptyBuckets()
    {
        while (_queue.Count > 0 && _queue.Peek().Count == 0)
        {
            var bucket = _queue.Dequeue();
            _buckets.Remove(bucket.DueTime);
        }
    }

    private DateTimeOffset? GetNextDueTime()
    {
        RemoveEmptyBuckets();
        return _queue.Count == 0 ? null : _queue.Peek().DueTime;
    }

    private void SignalQueueChangedIfNextDueTimeChanged(DateTimeOffset? previousNextDueTime)
    {
        if ((_isComplete && _jobCount == 0) || previousNextDueTime != GetNextDueTime())
        {
            SignalQueueChanged();
        }
    }

    private void SignalQueueChanged()
    {
        var waiter = _queueChangedWaiter;
        _queueChangedWaiter = null;
        waiter?.TrySetResult();
    }

    private Task GetQueueChangedTask()
        => (_queueChangedWaiter ??= CreateQueueChangedSource()).Task;

    private async Task WaitForQueueChangeOrDelayAsync(Task queueChanged, TimeSpan? delay, CancellationToken cancellationToken)
    {
        if (delay is null)
        {
            await queueChanged.WaitAsync(cancellationToken);
            return;
        }

        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var queueChangedTask = queueChanged.WaitAsync(waitCancellation.Token);
        var delayTask = Task.Delay(delay.Value, _timeProvider, waitCancellation.Token);
        var completedTask = await Task.WhenAny(queueChangedTask, delayTask);
        waitCancellation.Cancel();
        await completedTask;
    }

    private static TaskCompletionSource CreateQueueChangedSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class JobBucket
{
    private readonly Dictionary<string, (DurableJob Job, int DequeueCount)> _jobs = new();

    public int Count => _jobs.Count;

    public DateTimeOffset DueTime { get; private set; }

    public IEnumerable<(DurableJob Job, int DequeueCount)> Jobs => _jobs.Values;

    public JobBucket(DateTimeOffset dueTime)
    {
        DueTime = dueTime;
    }

    public void AddJob(DurableJob job, int dequeueCount)
    {
        _jobs[job.Id] = (job, dequeueCount);
    }

    public bool RemoveJob(string jobId)
    {
        return _jobs.Remove(jobId);
    }

    public bool TryGetJob(string jobId, out (DurableJob Job, int DequeueCount) job)
    {
        return _jobs.TryGetValue(jobId, out job);
    }

    public bool ContainsJob(DurableJob job)
        => _jobs.TryGetValue(job.Id, out var current) && ReferenceEquals(current.Job, job);
}
