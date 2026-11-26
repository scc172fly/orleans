using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 提供一份即时优先排队,根据应到的时间管理耐久工作。
/// 工作按时间分成桶,在到期时以不同步的方式列出。
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
    /// 获取当前队列中的工作总数 。
    /// </summary>
    public int Count => Volatile.Read(ref _jobCount);

    /// <summary>
    /// 将一个耐久任务添加到队列中,并带有指定的排队取消计数。
    /// </summary>
    /// <param name="job">长久的工作来排队。</param>
    /// <param name="dequeueCount">先前取消此工作排队的次数。</param>
    /// <exception cref="InvalidOperationException">尝试将工作排入完整队列时丢弃。</exception>
    /// <exception cref="ArgumentNullException">工作无效时丢弃。</exception>
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
    /// 标记队列为完整, 防止任何其它任务被排入队列 。
    /// 标记完成后,队列将完成剩余工作的处理,然后终止查点。
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
    /// 从队列中删除一个耐久的工作 。
    /// </summary>
    /// <param name="jobId">要删除的任务的唯一标识符。</param>
    /// <returns>如果找到并删除了工作, 则正确; 如果找不到工作, 则虚假。</returns>
    /// <remarks>
    /// 这份工作在处理之前,仍排在优先排队的排队中,但立即撤换了这份工作。
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
                // 尝试从桶中移除( 可能已经解排)
                bucket.RemoveJob(jobId);
                _jobsIdToBucket.Remove(jobId);
                Volatile.Write(ref _jobCount, _jobCount - 1);
                // 注:桶子在排队排队后一直留在优先位置,直至处理完毕
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
    /// 返回队列是否仍然包含已提供的持久工作。
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
    /// 重新安排工作, 以在新的到期时间重试 。
    /// </summary>
    /// <param name="jobContext">要重新尝试的工作的上下文。</param>
    /// <param name="newDueTime">新的工作到期时间。</param>
    /// <remarks>
    /// 这项工作从现有桶中移走,根据规定的时间添加到新的桶里。
    /// 保留了上下文的减缩计数。
    /// </remarks>
    public void RetryJobLater(IJobRunContext jobContext, DateTimeOffset newDueTime)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        _ = RetryJobLater(jobContext.Job.Id, newDueTime, jobContext.DequeueCount, executionGeneration: null);
    }

    /// <summary>
    /// 重新安排工作, 以在新的到期时间重试 。
    /// </summary>
    /// <param name="jobId">要重新尝试的工作的唯一标识符。</param>
    /// <param name="newDueTime">新的工作到期时间。</param>
    /// <param name="dequeueCount">与重新审阅的工作关联的已存在取消计数。</param>
    /// <returns>如果找到工作并重新安排了时间,是真实的;如果找不到工作,是虚假的。</returns>
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
    /// 获得一份即时的活工作简况,
    /// </summary>
    /// <returns>现有活的工作和取消计数。</returns>
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
    /// 清除所有队列状态 。
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
    /// 返回一个不同步的计算器, 产生当期可持久的工作。
    /// </summary>
    /// <param name="cancellationToken">用于监测取消请求的标记。</param>
    /// <returns>
    /// 返回到期任务 <see cref="IJobRunContext"/> 例的同步计算器。
    /// 当队列更改或下一个任务到期时, 数字员会醒来, 当队列标记完整和空时终止 。
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
                        yield break; // 如果队列被冻结和空, 退出
                    }

                    queueChanged = GetQueueChangedTask();
                }
                else if (_queue.Count > 0)
                {
                    var nextBucket = _queue.Peek();
                    var now = _timeProvider.GetUtcNow();
                    if (nextBucket.DueTime <= now)
                    {
                        // 卸下桶子,从原子上将其从 _bucket 中移除, 以同时方式将桶子脱下来
                        // 以同 DueTime 入队时,无法再使用此桶。没有这个,
                        // GetJobBucket 将发现桶里还有桶,并加添桶中。
                        // 桶已经不在了,所以新工作会被搁置
                        var bucketToProcess = _queue.Dequeue();
                        bucketBeingProcessed = bucketToProcess;
                        _buckets.Remove(bucketToProcess.DueTime);

                        // 抓图锁定下的工作, 并同时取消/ 重试突变
                        // 与列举的不相竞。
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
                // 处理门外桶里的所有工作,以更好地计算货币
                foreach (var (job, dequeueCount) in jobsToYield)
                {
                    // 核查我们处理期间没有取消工作
                    bool shouldYield;
                    lock (_syncLock)
                    {
                        shouldYield = bucketBeingProcessed is not null
                            && _jobsIdToBucket.TryGetValue(job.Id, out var currentBucket)
                            && ReferenceEquals(currentBucket, bucketBeingProcessed)
                            && bucketBeingProcessed.ContainsJob(job);
                        // 保持在 _ jobsIdToBucket 的工作, 通过 CancelJob/RetryJobLater明确删除
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
