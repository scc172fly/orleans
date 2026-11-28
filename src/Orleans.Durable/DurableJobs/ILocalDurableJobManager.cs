using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 提供在本地 Silo 上调度和管理作业的功能。
/// </summary>
public interface ILocalDurableJobManager
{
    /// <summary>
    /// 调度作业在指定时间由目标 Grain 执行。
    /// </summary>
    /// <param name="request">包含作业调度参数的请求。</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
    /// <returns>表示返回持久化作业的异步操作的 <see cref="Task"/>。</returns>
    /// <exception cref="ArgumentException"><see cref="ScheduleJobRequest.JobName"/> 为 null、空字符串或仅包含空白字符。</exception>
    Task<DurableJob> ScheduleJobAsync(ScheduleJobRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 请求取消此前调度的持久化作业。
    /// </summary>
    /// <param name="job">请求取消的持久化作业。</param>
    /// <param name="requestCancellationToken">
    /// 用于取消此请求操作的令牌。它不会取消作业执行尝试。
    /// </param>
    /// <returns>
    /// 表示异步操作的 <see cref="Task"/>。结果为 <see langword="true"/> 表示
    /// 取消请求已持久化记录，阻止后续执行尝试；否则结果为 <see langword="false"/>。
    /// 已在运行的执行尝试与此请求协作式解耦，仍可能完成。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="job"/> 为 <see langword="null"/>。</exception>
    Task<bool> CancelAsync(DurableJob job, CancellationToken requestCancellationToken);
}

internal interface ILocalDurableJobManagerSystemTarget : ISystemTarget
{
    Task<bool> CancelAsync(DurableJob job, CancellationToken requestCancellationToken);
}
