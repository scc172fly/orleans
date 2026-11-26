using System;
using System.Diagnostics;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 单曲调 <see cref="IDurableJobHandler.ExecuteJobAsync"/>,记录
/// 通过 <see cref="DurableJobsInstruments"/> 和分布式跟踪活动
/// 经由 <see cref="DurableJobsDiagnostics"/>。调用者必须准确记录一个终端遥测结果
/// 使用 <see cref="RecordResult"/>、 <see cref="Completed"/>、 <see cref="AttemptCanceled"/>或 <see cref="Failed"/>
/// 处理前。
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
    /// 记录失败结果的遥测失败,并且完成每个未失败处置的遥测。
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
    /// 将成功执行的处理器记录下来,并将活动标为 <see cref="ActivityStatusCode.Ok"/>。
    /// </summary>
    public void Completed()
    {
        _durableJobsInstruments.OnHandlerExecutionCompleted(_timeProvider.GetElapsedTime(_startTimestamp));
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// 合作取消当前处理器执行尝试的记录。
    /// 处置保留默认(未设置)状态,与原始执行的行为相符。
    /// </summary>
    public void AttemptCanceled()
    {
        _durableJobsInstruments.OnHandlerExecutionAttemptCanceled(_timeProvider.GetElapsedTime(_startTimestamp));
    }

    /// <summary>
    /// 记录处理器执行失败,并将提供的例外附加到活动上。
    /// </summary>
    public void Failed(Exception exception)
    {
        _durableJobsInstruments.OnHandlerExecutionFailed(_timeProvider.GetElapsedTime(_startTimestamp));
        DurableJobsDiagnostics.SetError(_activity, exception);
    }

    public void Dispose() => _activity?.Dispose();
}
