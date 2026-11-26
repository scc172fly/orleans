using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 能够接受持久工作职业的 grains 扩展界面。
/// </summary>
internal interface IDurableJobReceiverExtension : IGrainExtension
{
    /// <summary>
    /// 处理一项持久的工作,要么开始执行,要么检查仍在执行中的执行状况。
    /// 同一工作同时分娩的同工同工同工同用。
    /// 终端处置,较晚交货开始新的援引。
    /// </summary>
    /// <param name="context">包含有关持久工作的信息的背景。</param>
    /// <param name="attemptCancellationToken">
    /// 合作要求取消这一处决企图的象征。
    /// 取消企图使持久工作有资格重新交付。
    /// </param>
    /// <returns>代表非同步操作并包含工作执行结果的任务。</returns>
    [AlwaysInterleave]
    ValueTask<DurableJobRunResult> HandleDurableJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken);
}

/// <inheritdoc />
internal sealed partial class DurableJobReceiverExtension : IDurableJobReceiverExtension
{
    private readonly IGrainContext _grain;
    private readonly DurableJobReceiverExtensionShared _shared;
    private readonly IDurableJobHandlerLookup _featureHandlers;
    private readonly Dictionary<(string JobId, long ExecutionGeneration, int DequeueCount), JobAttemptState> _jobAttempts = [];

    public DurableJobReceiverExtension(
        IGrainContext grain,
        DurableJobReceiverExtensionShared shared,
        IDurableJobHandlerLookup? featureHandlers = null)
    {
        ArgumentNullException.ThrowIfNull(grain);
        ArgumentNullException.ThrowIfNull(shared);

        _grain = grain;
        _shared = shared;
        _featureHandlers = featureHandlers ?? new DurableJobHandlerRegistry();
    }

    /// <inheritdoc />
    public ValueTask<DurableJobRunResult> HandleDurableJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = GetExecutionKey(context);
        var newJob = false;
        if (!_jobAttempts.TryGetValue(key, out var state))
        {
            state = new JobAttemptState(StartJob(context, attemptCancellationToken));
            _jobAttempts.Add(key, state);
            newJob = true;
        }
        else if (state.Task.IsCanceled && !attemptCancellationToken.IsCancellationRequested)
        {
            state = new JobAttemptState(StartJob(context, attemptCancellationToken));
            _jobAttempts[key] = state;
            newJob = true;
        }
        else if (IsReadyToPoll(state))
        {
            state = new JobAttemptState(StartJob(context, attemptCancellationToken));
            _jobAttempts[key] = state;
            newJob = true;
        }

        Debug.Assert(state is not null);
        return GetJobStatusAsync(key, context, state, newJob, attemptCancellationToken);
    }

    private bool IsReadyToPoll(JobAttemptState state) =>
        state.PollRequested
        && _shared.TimeProvider.GetElapsedTime(state.PollTimestamp, _shared.TimeProvider.GetTimestamp()) >= state.PollAfterDelay;

    private Task<DurableJobRunResult> StartJob(IJobRunContext context, CancellationToken attemptCancellationToken)
    {
        if (_featureHandlers.TryGetHandler(context.Job.Name, out var featureHandler))
        {
            return ExecuteFeatureHandlerAsync(featureHandler, context, attemptCancellationToken);
        }

        if (_grain.GrainInstance is not IDurableJobHandler handler)
        {
            LogGrainDoesNotImplementHandler(_shared.Logger, _grain.GrainId);
            throw new InvalidOperationException($"Grain {_grain.GrainId} does not implement IDurableJobHandler");
        }

        return ExecuteHandlerAsync(handler, context, attemptCancellationToken);
    }

    private Task<DurableJobRunResult> ExecuteFeatureHandlerAsync(
        IDurableJobFeatureHandler handler,
        IJobRunContext context,
        CancellationToken attemptCancellationToken) =>
        ExecuteHandlerAsync(
            context,
            attemptCancellationToken,
            () => handler.ExecuteJobAsync(context, attemptCancellationToken));

    private Task<DurableJobRunResult> ExecuteHandlerAsync(
        IDurableJobHandler handler,
        IJobRunContext context,
        CancellationToken attemptCancellationToken)
    {
        return ExecuteHandlerAsync(context, attemptCancellationToken, ExecuteAsync);

        async ValueTask<DurableJobRunResult> ExecuteAsync()
        {
            await handler.ExecuteJobAsync(context, attemptCancellationToken);
            return DurableJobRunResult.Completed;
        }
    }

    private async Task<DurableJobRunResult> ExecuteHandlerAsync(
        IJobRunContext context,
        CancellationToken attemptCancellationToken,
        Func<ValueTask<DurableJobRunResult>> execute)
    {
        using var tracker = _shared.BeginHandlerExecution(context);
        try
        {
            var result = await execute()
                ?? throw new InvalidOperationException($"Durable job handler for '{context.Job.Name}' returned a null result.");
            tracker.RecordResult(result);
            return result;
        }
        catch (OperationCanceledException) when (attemptCancellationToken.IsCancellationRequested)
        {
            // 取消企图使持久工作有资格重新交付。
            tracker.AttemptCanceled();
            throw;
        }
        catch (Exception exception)
        {
            tracker.Failed(exception);
            LogErrorExecutingDurableJob(_shared.Logger, exception, context.Job.Id, _grain.GrainId);
            return DurableJobRunResult.Failed(exception);
        }
    }

    private ValueTask<DurableJobRunResult> GetJobStatusAsync(
        (string JobId, long ExecutionGeneration, int DequeueCount) key,
        IJobRunContext context,
        JobAttemptState state,
        bool newJob,
        CancellationToken attemptCancellationToken)
    {
        // 取消是合作性的:只有最终任务国才有权决定工作结果。
        if (!state.Task.IsCompleted)
        {
            if (newJob)
            {
                // 第一次尝试是减少RPC, 我们等待投票间隔或一半的响应超时完成任务。
                // 这对于工作快速完成的常见情况省下后退。
                return LongPollGetJobStatusAsync(key, context, state, attemptCancellationToken);
            }

            return new(DurableJobRunResult.InProgress(_shared.Options.JobStatusPollInterval));
        }

        if (state.Task.IsCompletedSuccessfully)
        {
            return new(GetSuccessfulResult(key, state));
        }

        RemoveJobAttempt(key, state);

        if (state.Task.IsFaulted)
        {
            var ex = state.Task.Exception!.InnerException ?? state.Task.Exception;
            LogErrorExecutingDurableJob(_shared.Logger, ex, context.Job.Id, _grain.GrainId);
            return new(DurableJobRunResult.Failed(ex));
        }

        return ValueTask.FromCanceled<DurableJobRunResult>(new CancellationToken(canceled: true));

        async ValueTask<DurableJobRunResult> LongPollGetJobStatusAsync(
            (string JobId, long ExecutionGeneration, int DequeueCount) key,
            IJobRunContext context,
            JobAttemptState state,
            CancellationToken attemptCancellationToken)
        {
            if (!state.Task.IsCompleted)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(attemptCancellationToken);
                var longPollDuration = TimeSpan.FromTicks(Math.Min(_shared.MessagingOptions.ResponseTimeout.Divide(2).Ticks, _shared.Options.JobStatusPollInterval.Ticks));
                await Task.WhenAny(Task.Delay(longPollDuration, _shared.TimeProvider, cts.Token), state.Task);
                cts.Cancel();

                if (!state.Task.IsCompleted)
                {
                    return DurableJobRunResult.InProgress(_shared.Options.JobStatusPollInterval);
                }
            }

            if (state.Task.IsFaulted)
            {
                RemoveJobAttempt(key, state);
                var ex = state.Task.Exception!.InnerException ?? state.Task.Exception;
                LogErrorExecutingDurableJob(_shared.Logger, ex, context.Job.Id, _grain.GrainId);
                return DurableJobRunResult.Failed(ex);
            }

            if (state.Task.IsCanceled)
            {
                RemoveJobAttempt(key, state);
                return await state.Task;
            }

            return GetSuccessfulResult(key, state);
        }
    }

    private DurableJobRunResult GetSuccessfulResult(
        (string JobId, long ExecutionGeneration, int DequeueCount) key,
        JobAttemptState state)
    {
        var result = state.Task.Result;
        if (result.IsInProgress)
        {
            if (!state.PollRequested)
            {
                state.PollRequested = true;
                state.PollTimestamp = _shared.TimeProvider.GetTimestamp();
                state.PollAfterDelay = result.PollAfterDelay.Value;
            }
        }
        else
        {
            RemoveJobAttempt(key, state);
        }

        return result;
    }

    private void RemoveJobAttempt((string JobId, long ExecutionGeneration, int DequeueCount) key, JobAttemptState state)
    {
        if (_jobAttempts.TryGetValue(key, out var current) && ReferenceEquals(current, state))
        {
            _jobAttempts.Remove(key);
        }
    }

    private static (string JobId, long ExecutionGeneration, int DequeueCount) GetExecutionKey(IJobRunContext context)
        => (context.Job.Id, context.Job.ExecutionGeneration, context.DequeueCount);

    internal sealed class TestAccessor(DurableJobReceiverExtension extension)
    {
        public Task<DurableJobRunResult>? GetAttemptTask(IJobRunContext context) =>
            extension._jobAttempts.TryGetValue(GetExecutionKey(context), out var state) ? state.Task : null;
    }

    private sealed class JobAttemptState(Task<DurableJobRunResult> task)
    {
        public Task<DurableJobRunResult> Task { get; } = task;

        public bool PollRequested;

        public long PollTimestamp;

        public TimeSpan PollAfterDelay;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing durable job {JobId} on grain {GrainId}")]
    private static partial void LogErrorExecutingDurableJob(ILogger logger, Exception exception, string jobId, GrainId grainId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Grain {GrainId} does not implement IDurableJobHandler")]
    private static partial void LogGrainDoesNotImplementHandler(ILogger logger, GrainId grainId);
}
