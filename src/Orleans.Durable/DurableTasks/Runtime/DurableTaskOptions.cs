using System;

namespace Orleans.Configuration;

/// <summary>配置耐久 grain RPC的保留和清理。</summary>
public sealed class DurableTaskOptions
{
    /// <summary>获取或确定完成后投票时还有多长时间的终站答复。</summary>
    /// <remarks>
    /// 适用于没有 grain 调用器的任务,例如客户预定的任务,以及其完成信息
    /// 任务被用死字母来写, 从废信件中测量。 当时间结束时, 任务会随着执行而删除
    /// 树: 民意测验报告 <see cref="Orleans.Durable.Tasks.DurableTaskNotFoundException"/>,并安排相同任务
    /// 标识符再次执行,因此任务识别符在完成后会更改此时期的进度表。
    /// 时间段不与 <c>DeduplicationWindow</c>:任务标识符的每个运行都有自己的
    /// 其信息所覆盖的内化,即其信息的身份所覆盖的内化,因此以后一期版本的信息将发送,对
    /// 早跑还没到
    /// 完成任务后,在服务结束后,将删除其调用 grain 。
    /// </remarks>
    public TimeSpan ResultRetentionPeriod { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// 获取或设置 grain 使一个后期的启动重弹
    /// 执行。
    /// </summary>
    /// <remarks>
    /// 当运行中的代码无法重放时, 执行摊位会被重播: 它记录了一个版本, 而代码不支持它,
    /// 或其记录有出入的代码。任务将等待完成, grain 在此间隔时保持警醒, 所以
    /// 一旦其启动结束,例如部署停止时,即停止 silo, 和 grain 恢复和重放执行
    /// 与新激活的代码运行。 间隔为正数; 默认值为1分钟 。
    /// </remarks>
    public TimeSpan StalledExecutionRetryInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取或设定执行时在行刑后休眠时停止的 grain 多久才会被控制, 以便稍后激活
    /// 恢复执行。
    /// </summary>
    /// <remarks>
    /// 而在 grain处决运行代码, grain 继续在此延误时提醒人们注意“持久工作”。 grain 减去
    /// 延迟过后,会同时发生崩溃或停止作用,重新启动和重播处决。 grain
    /// 活动,每次醒醒只重新安排时间本身,所以延迟也决定了发生频率。
    /// 等待诸如呼叫结果等信息,不需要提醒:该信息重新启动 grain。延迟
    /// 是正数;默认值为30秒。
    /// </remarks>
    public TimeSpan RunnableExecutionWakeupDelay { get; set; } = TimeSpan.FromSeconds(30);
}
