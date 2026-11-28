using System;
using System.Diagnostics;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 跟踪对 <see cref="IDurableJobHandler.ExecuteJobAsync"/> 的单次调用，通过
/// <see cref="DurableJobsInstruments"/> 记录相应指标，并通过
/// <see cref="DurableJobsDiagnostics"/> 记录分布式跟踪 Activity。调用方必须
/// 使用 <see cref="RecordResult"/>、<see cref="Completed"/>、<see cref="AttemptCanceled"/> 或 <see cref="Failed"/> 恰好记录一次终态遥测结果，
/// 然后才能释放跟踪器。
/// </summary>
internal readonly struct HandlerExecutionTracker : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly DurableJobsInstruments _durableJobsInstruments;
    private readonly long _startTimestamp;
    private readonly Activity? _activity;

    public HandlerExecutionTracker(TimeProvider timeProvider, DurableJobsInstruments durableJobsInstruments, IJobRunContext context)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(durableJobsInstruments);
        ArgumentNullException.ThrowIfNull(context);

        _timeProvider = timeProvider;
        _durableJobsInstruments = durableJobsInstruments;
        _startTimestamp = timeProvider.GetTimestamp();
        _durableJobsInstruments.OnHandlerExecutionStarted();
        _activity = DurableJobsDiagnostics.StartHandlerActivity(context.Job, context.DequeueCount, context.RunId);
    }

    /// <summary>
    /// 对失败结果记录失败遥测，对任何非失败的处置结果记录完成遥测。
    /// </summary>
    public void RecordResult(DurableJobRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailed)
        {
            Failed(result.Exception);
        }
        else
        {
            Completed();
        }
    }

    /// <summary>
    /// 记录处理程序成功执行，并将 Activity 标记为 <see cref="ActivityStatusCode.Ok"/>。
    /// </summary>
    public void Completed()
    {
        _durableJobsInstruments.OnHandlerExecutionCompleted(_timeProvider.GetElapsedTime(_startTimestamp));
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// 记录当前处理程序执行尝试的协作式取消。Activity 状态保持不变，
    /// 使释放操作保留默认的未设置状态，与原实现的行为一致。
    /// </summary>
    public void AttemptCanceled()
    {
        _durableJobsInstruments.OnHandlerExecutionAttemptCanceled(_timeProvider.GetElapsedTime(_startTimestamp));
    }

    /// <summary>
    /// 记录处理程序执行失败，并将指定异常附加到 Activity。
    /// </summary>
    public void Failed(Exception exception)
    {
        _durableJobsInstruments.OnHandlerExecutionFailed(_timeProvider.GetElapsedTime(_startTimestamp));
        DurableJobsDiagnostics.SetError(_activity, exception);
    }

    public void Dispose() => _activity?.Dispose();
}
