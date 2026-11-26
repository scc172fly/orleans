using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 提供本地 silo安排和管理工作的功能。
/// </summary>
public interface ILocalDurableJobManager
{
    /// <summary>
    /// 安排在目标 grain特定时间执行的工作。
    /// </summary>
    /// <param name="request">载有工作时间安排参数的请求。</param>
    /// <param name="cancellationToken">取消操作的取消符号。</param>
    /// <returns>A <see cref="Task"/> 代表着无序的操作 返回了持久的工作。</returns>
    /// <exception cref="ArgumentException"><see cref="ScheduleJobRequest.JobName"/> 为空空空空空空空空空。</exception>
    Task<DurableJob> ScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 要求取消先前排定的持久工作。
    /// </summary>
    /// <param name="job">要求取消的持久工作。</param>
    /// <param name="requestCancellationToken">
    /// 取消此请求操作的符号。 它不会取消执行任务尝试 。
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> 代表非同步操作。结果为 <see langword="true"/> 何时
    /// 注销请求有可持久记录,防止今后再作尝试;否则, <see langword="false"/>.
    /// 一项已经进行中的尝试是合作性地独立于这一要求之外,可能仍然完成。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="job"/> 是 <see langword="null"/>.</exception>
    Task<bool> CancelAsync(DurableJob job, CancellationToken requestCancellationToken);
}

internal interface ILocalDurableJobManagerSystemTarget : ISystemTarget
{
    Task<bool> CancelAsync(DurableJob job, CancellationToken requestCancellationToken);
}
