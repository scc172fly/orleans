using System;

namespace Orleans.Configuration;

/// <summary>配置持久化 Grain RPC 的保留与清理。</summary>
public sealed class DurableTaskOptions
{
    /// <summary>获取或设置终态响应在完成后仍可供轮询的时长。</summary>
    /// <remarks>
    /// 适用于没有 Grain 调用方的任务（例如客户端调度的任务），以及其完成消息
    /// 已转为死信的任务；后一种情况从死信产生时起计时。保留期结束后，任务及其执行树将被删除，
    /// 轮询报告 <see cref="Orleans.Durable.Tasks.DurableTaskNotFoundException"/>，而调度具有相同任务
    /// 标识符的任务会再次执行它，因此任务标识符在完成后的此期间对调度进行去重。该
    /// 保留期独立于 Messaging 的 <c>DeduplicationWindow</c>：同一任务标识符的每次运行都有自己的
    /// 运行世代标识，且消息标识包含该标识，因此后续运行的消息会被投递，而发往
    /// 先前运行的回复不会到达后续运行。
    /// 若任务的完成通知已投递到其调用方 Grain，则在投递退役时删除任务。
    /// </remarks>
    public TimeSpan ResultRetentionPeriod { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// 获取或设置保存停滞执行实例的 Grain 的唤醒频率，以便后续激活实例重放该
    /// 执行实例。
    /// </summary>
    /// <remarks>
    /// 当正在运行的代码无法重放某个执行实例时，该执行实例停滞：它记录了代码不支持的版本，
    /// 或者代码与其记录发生分歧。任务保持待处理状态，Grain 按此间隔保留唤醒操作，因此
    /// 当其激活实例结束时（例如部署导致其 Silo 停止），Grain 会重新激活，并使用新激活实例运行的
    /// 代码重放执行实例。间隔必须为正值，默认一分钟。
    /// </remarks>
    public TimeSpan StalledExecutionRetryInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设置 Grain 在有执行实例运行时停止后多快唤醒，以便后续激活实例
    /// 恢复执行实例。
    /// </summary>
    /// <remarks>
    /// Grain 的某个执行实例运行代码时，该 Grain 会按此延迟保持 Durable Jobs 唤醒操作。若 Grain
    /// 在此期间崩溃或停用，延迟过去后会重新激活并重放该执行实例。Grain
    /// 处于活动状态，则每次唤醒只会重新调度自身，因此该延迟也决定了重新调度的频率。已挂起的执行实例
    /// 正在等待消息（例如调用结果）时，无需唤醒操作：消息会重新激活其 Grain。此延迟
    /// 必须为正值，默认为 30 秒。
    /// </remarks>
    public TimeSpan RunnableExecutionWakeupDelay { get; set; } = TimeSpan.FromSeconds(30);
}
