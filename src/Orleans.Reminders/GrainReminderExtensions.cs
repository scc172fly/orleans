using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Timers;

namespace Orleans;

/// <summary>
/// 获取 <see cref="Grain"/> 或 <see cref="IGrainBase"/> 执行过程中的提醒通知的扩展方法。
/// </summary>
public static class GrainReminderExtensions
{
    /// <summary>
    /// 登记一项持久可靠的催复通知,以便定期向 grain发出通知(提醒)。
    /// 下 grain 执行必须: <c>Orleans.IRemindable</c> 界面,以及为此提醒 grain 发送到 <c>ReceiveReminder</c> 回调方法 。
    /// 如果当定时器火灾时当前 grain 停止使用,将重新激活此 grain ,以接收提醒。
    /// 如果已有一个名称相同的现有提醒,则该提醒将随此新的提醒而被覆盖。
    /// 提醒总是通过激活一次收到,即使该 grain有多个启动 grain。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒通知的名称</param>
    /// <param name="dueTime">提醒的到期时间。 <see cref="TimeSpan.Zero"/> 值立即将第一个勾选计数;负值、无限值或超过剩余 <see cref="DateTime"/> 范围的数值被否决。</param>
    /// 此提醒的<param name="period">频率周期。 值必须至少为 <see cref="Orleans.Hosting.ReminderOptions.MinimumReminderPeriod"/>; 小数值和 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 被否决。</param>
    /// <returns>承诺提醒处理器。</returns>
    /// <remarks>
    /// <paramref name="period"/>没有特殊的一次性提醒值。 登记一个有效的正数周期, 然后在第一次回调火灾时解除提醒 。
    /// </remarks>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this Grain grain, string reminderName, TimeSpan dueTime, TimeSpan period)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, CancellationToken.None);

    /// <summary>登记或更新催复通知。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="dueTime">时机已到</param>
    /// <param name="period">这段时间</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// 提醒<returns>。</returns>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this Grain grain, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, cancellationToken);

    /// <summary>
    /// 登记一项持久可靠的催复通知,以便定期向 grain发出通知(提醒)。
    /// 下 grain 执行必须: <c>Orleans.IRemindable</c> 界面,以及为此提醒 grain 发送到 <c>ReceiveReminder</c> 回调方法 。
    /// 如果当定时器火灾时当前 grain 停止使用,将重新激活此 grain ,以接收提醒。
    /// 如果已有一个名称相同的现有提醒,则该提醒将随此新的提醒而被覆盖。
    /// 提醒总是通过激活一次收到,即使该 grain有多个启动 grain。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒通知的名称</param>
    /// <param name="dueTime">提醒的到期时间。 <see cref="TimeSpan.Zero"/> 值立即将第一个勾选计数;负值、无限值或超过剩余 <see cref="DateTime"/> 范围的数值被否决。</param>
    /// 此提醒的<param name="period">频率周期。 值必须至少为 <see cref="Orleans.Hosting.ReminderOptions.MinimumReminderPeriod"/>; 小数值和 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 被否决。</param>
    /// <returns>承诺提醒处理器。</returns>
    /// <remarks>
    /// <paramref name="period"/>没有特殊的一次性提醒值。 登记一个有效的正数周期, 然后在第一次回调火灾时解除提醒 。
    /// </remarks>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this IGrainBase grain, string reminderName, TimeSpan dueTime, TimeSpan period)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, CancellationToken.None);

    /// <summary>登记或更新催复通知。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="dueTime">时机已到</param>
    /// <param name="period">这段时间</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// 提醒<returns>。</returns>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this IGrainBase grain, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, cancellationToken);

    private static Task<IGrainReminder> RegisterOrUpdateReminder(
        bool remindable,
        IGrainContext? grainContext,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainContext, "grain");
        if (string.IsNullOrWhiteSpace(reminderName)) throw new ArgumentNullException(nameof(reminderName));
        if (!remindable) throw new InvalidOperationException($"Grain {grainContext.GrainId} is not '{nameof(IRemindable)}'. A grain should implement {nameof(IRemindable)} to use the persistent reminder service");

        return GetReminderRegistry(grainContext).RegisterOrUpdateReminder(grainContext.GrainId, reminderName, dueTime, period, cancellationToken);
    }

    /// <summary>
    /// 以往已登记的催复通知,如不登记,则不予登记。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminder">提醒不登记。</param>
    /// <returns>这一行动的完成承诺。</returns>
    public static Task UnregisterReminder(this Grain grain, IGrainReminder reminder) => UnregisterReminder(grain?.GrainContext, reminder, CancellationToken.None);

    /// <summary>以往已登记的催复通知,如不登记,则不予登记。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// 提醒<param name="reminder">。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>代表操作的任务。</returns>
    public static Task UnregisterReminder(this Grain grain, IGrainReminder reminder, CancellationToken cancellationToken)
        => UnregisterReminder(grain?.GrainContext, reminder, cancellationToken);

    /// <summary>
    /// 以往已登记的催复通知,如不登记,则不予登记。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminder">提醒不登记。</param>
    /// <returns>这一行动的完成承诺。</returns>
    public static Task UnregisterReminder(this IGrainBase grain, IGrainReminder reminder) => UnregisterReminder(grain?.GrainContext, reminder, CancellationToken.None);

    /// <summary>以往已登记的催复通知,如不登记,则不予登记。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// 提醒<param name="reminder">。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>代表操作的任务。</returns>
    public static Task UnregisterReminder(this IGrainBase grain, IGrainReminder reminder, CancellationToken cancellationToken)
        => UnregisterReminder(grain?.GrainContext, reminder, cancellationToken);

    private static Task UnregisterReminder(IGrainContext? grainContext, IGrainReminder reminder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainContext, "grain");
        return GetReminderRegistry(grainContext).UnregisterReminder(grainContext.GrainId, reminder, cancellationToken);
    }

    /// <summary>
    /// a 返回先前登记的催复通知。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒返回</param>
    /// <returns>承诺提醒处理器。</returns>
    public static Task<IGrainReminder?> GetReminder(this Grain grain, string reminderName) => GetReminder(grain?.GrainContext, reminderName, CancellationToken.None);

    /// <summary>返回先前登记的提醒函。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// 提醒<returns>。</returns>
    public static Task<IGrainReminder?> GetReminder(this Grain grain, string reminderName, CancellationToken cancellationToken)
        => GetReminder(grain?.GrainContext, reminderName, cancellationToken);

    /// <summary>
    /// a 返回先前登记的催复通知。
    /// </summary>
    /// <param name="grain">A grain.</param>
    /// <param name="reminderName">提醒返回</param>
    /// <returns>承诺提醒处理器。</returns>
    public static Task<IGrainReminder?> GetReminder(this IGrainBase grain, string reminderName) => GetReminder(grain?.GrainContext, reminderName, CancellationToken.None);

    /// <summary>返回先前登记的提醒函。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// 提醒<returns>。</returns>
    public static Task<IGrainReminder?> GetReminder(this IGrainBase grain, string reminderName, CancellationToken cancellationToken)
        => GetReminder(grain?.GrainContext, reminderName, cancellationToken);

    private static Task<IGrainReminder?> GetReminder(IGrainContext? grainContext, string reminderName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainContext, "grain");
        if (string.IsNullOrWhiteSpace(reminderName)) throw new ArgumentNullException(nameof(reminderName));

        return GetReminderRegistry(grainContext).GetReminder(grainContext.GrainId, reminderName, cancellationToken);
    }

    /// <summary>
    /// 返回 grain登记的所有提醒通知的清单。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <returns>承诺提供为此登记了提醒措施清单 grain.</returns>
    public static Task<List<IGrainReminder>> GetReminders(this Grain grain) => GetReminders(grain?.GrainContext, CancellationToken.None);

    /// <summary>返回 grain登记的所有提醒。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>提醒函。</returns>
    public static Task<List<IGrainReminder>> GetReminders(this Grain grain, CancellationToken cancellationToken)
        => GetReminders(grain?.GrainContext, cancellationToken);

    /// <summary>
    /// 返回 grain登记的所有提醒通知的清单。
    /// </summary>
    /// <param name="grain"> grain 实例。</param>
    /// <returns>承诺提供为此登记了提醒措施清单 grain.</returns>
    public static Task<List<IGrainReminder>> GetReminders(this IGrainBase grain) => GetReminders(grain?.GrainContext, CancellationToken.None);

    /// <summary>返回 grain登记的所有提醒。</summary>
    /// <param name="grain"> grain 实例。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>提醒函。</returns>
    public static Task<List<IGrainReminder>> GetReminders(this IGrainBase grain, CancellationToken cancellationToken)
        => GetReminders(grain?.GrainContext, cancellationToken);

    private static Task<List<IGrainReminder>> GetReminders(IGrainContext? grainContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainContext, "grain");
        return GetReminderRegistry(grainContext).GetReminders(grainContext.GrainId, cancellationToken);
    }

    /// <summary>
    /// 获取 <see cref="IReminderRegistry"/>。
    /// </summary>
    private static IReminderRegistry GetReminderRegistry(IGrainContext grainContext)
    {
        return grainContext.ActivationServices.GetRequiredService<IReminderRegistry>();
    }
}
