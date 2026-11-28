using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Timers;

namespace Orleans;

/// <summary>
/// 用于从 <see cref="Grain"/> 或 <see cref="IGrainBase"/> 的实现访问提醒器的扩展方法。
/// </summary>
public static class GrainReminderExtensions
{
    /// <summary>
    /// 注册一个持久化、可靠的提醒器，向 Grain 定期发送通知（提醒）。
    /// Grain 必须实现 <c>Orleans.IRemindable</c> 接口，此 Grain 的提醒将发送至 <c>ReceiveReminder</c> 回调方法。
    /// 如果定时器触发时当前 Grain 已停用，则创建该 Grain 的新激活实例以接收提醒。
    /// 如果已存在同名提醒器，新提醒器将覆盖现有提醒器。
    /// 即使此 Grain 存在多个激活实例，提醒也始终由其中一个激活实例接收。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">此提醒器的名称。</param>
    /// <param name="dueTime">此提醒器首次触发的等待时间。<see cref="TimeSpan.Zero"/> 表示立即调度首次触发；负值、无限值或超出 <see cref="DateTime"/> 剩余范围的值均会被拒绝。</param>
    /// <param name="period">此提醒器的触发周期。该值必须至少为 <see cref="Orleans.Hosting.ReminderOptions.MinimumReminderPeriod"/>；更小的值和 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 均会被拒绝。</param>
    /// <returns>用于获取提醒器句柄的异步操作。</returns>
    /// <remarks>
    /// <paramref name="period"/> 没有用于单次提醒的特殊值。请注册一个有效的正数周期，然后在首次回调触发时注销提醒器。
    /// </remarks>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this Grain grain, string reminderName, TimeSpan dueTime, TimeSpan period)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, CancellationToken.None);

    /// <summary>注册或更新提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="dueTime">首次触发的等待时间。</param>
    /// <param name="period">周期。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提醒器。</returns>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this Grain grain, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, cancellationToken);

    /// <summary>
    /// 注册一个持久化、可靠的提醒器，向 Grain 定期发送通知（提醒）。
    /// Grain 必须实现 <c>Orleans.IRemindable</c> 接口，此 Grain 的提醒将发送至 <c>ReceiveReminder</c> 回调方法。
    /// 如果定时器触发时当前 Grain 已停用，则创建该 Grain 的新激活实例以接收提醒。
    /// 如果已存在同名提醒器，新提醒器将覆盖现有提醒器。
    /// 即使此 Grain 存在多个激活实例，提醒也始终由其中一个激活实例接收。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">此提醒器的名称。</param>
    /// <param name="dueTime">此提醒器首次触发的等待时间。<see cref="TimeSpan.Zero"/> 表示立即调度首次触发；负值、无限值或超出 <see cref="DateTime"/> 剩余范围的值均会被拒绝。</param>
    /// <param name="period">此提醒器的触发周期。该值必须至少为 <see cref="Orleans.Hosting.ReminderOptions.MinimumReminderPeriod"/>；更小的值和 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 均会被拒绝。</param>
    /// <returns>用于获取提醒器句柄的异步操作。</returns>
    /// <remarks>
    /// <paramref name="period"/> 没有用于单次提醒的特殊值。请注册一个有效的正数周期，然后在首次回调触发时注销提醒器。
    /// </remarks>
    public static Task<IGrainReminder> RegisterOrUpdateReminder(this IGrainBase grain, string reminderName, TimeSpan dueTime, TimeSpan period)
        => RegisterOrUpdateReminder(grain is IRemindable, grain?.GrainContext, reminderName, dueTime, period, CancellationToken.None);

    /// <summary>注册或更新提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="dueTime">首次触发的等待时间。</param>
    /// <param name="period">周期。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提醒器。</returns>
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
    /// 注销先前已注册的提醒器。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminder">要注销的提醒器。</param>
    /// <returns>表示此操作完成情况的异步任务。</returns>
    public static Task UnregisterReminder(this Grain grain, IGrainReminder reminder) => UnregisterReminder(grain?.GrainContext, reminder, CancellationToken.None);

    /// <summary>注销先前已注册的提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminder">提醒器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的任务。</returns>
    public static Task UnregisterReminder(this Grain grain, IGrainReminder reminder, CancellationToken cancellationToken)
        => UnregisterReminder(grain?.GrainContext, reminder, cancellationToken);

    /// <summary>
    /// 注销先前已注册的提醒器。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminder">要注销的提醒器。</param>
    /// <returns>表示此操作完成情况的异步任务。</returns>
    public static Task UnregisterReminder(this IGrainBase grain, IGrainReminder reminder) => UnregisterReminder(grain?.GrainContext, reminder, CancellationToken.None);

    /// <summary>注销先前已注册的提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminder">提醒器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的任务。</returns>
    public static Task UnregisterReminder(this IGrainBase grain, IGrainReminder reminder, CancellationToken cancellationToken)
        => UnregisterReminder(grain?.GrainContext, reminder, cancellationToken);

    private static Task UnregisterReminder(IGrainContext? grainContext, IGrainReminder reminder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainContext, "grain");
        return GetReminderRegistry(grainContext).UnregisterReminder(grainContext.GrainId, reminder, cancellationToken);
    }

    /// <summary>
    /// 返回先前已注册的提醒器。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">要返回的提醒器的名称。</param>
    /// <returns>用于获取提醒器句柄的异步操作。</returns>
    public static Task<IGrainReminder?> GetReminder(this Grain grain, string reminderName) => GetReminder(grain?.GrainContext, reminderName, CancellationToken.None);

    /// <summary>返回先前已注册的提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提醒器。</returns>
    public static Task<IGrainReminder?> GetReminder(this Grain grain, string reminderName, CancellationToken cancellationToken)
        => GetReminder(grain?.GrainContext, reminderName, cancellationToken);

    /// <summary>
    /// 返回先前已注册的提醒器。
    /// </summary>
    /// <param name="grain">一个 Grain。</param>
    /// <param name="reminderName">要返回的提醒器的名称。</param>
    /// <returns>用于获取提醒器句柄的异步操作。</returns>
    public static Task<IGrainReminder?> GetReminder(this IGrainBase grain, string reminderName) => GetReminder(grain?.GrainContext, reminderName, CancellationToken.None);

    /// <summary>返回先前已注册的提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提醒器。</returns>
    public static Task<IGrainReminder?> GetReminder(this IGrainBase grain, string reminderName, CancellationToken cancellationToken)
        => GetReminder(grain?.GrainContext, reminderName, cancellationToken);

    private static Task<IGrainReminder?> GetReminder(IGrainContext? grainContext, string reminderName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grainContext, "grain");
        if (string.IsNullOrWhiteSpace(reminderName)) throw new ArgumentNullException(nameof(reminderName));

        return GetReminderRegistry(grainContext).GetReminder(grainContext.GrainId, reminderName, cancellationToken);
    }

    /// <summary>
    /// 返回该 Grain 注册的所有提醒器的列表。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <returns>用于获取此 Grain 已注册提醒器列表的异步操作。</returns>
    public static Task<List<IGrainReminder>> GetReminders(this Grain grain) => GetReminders(grain?.GrainContext, CancellationToken.None);

    /// <summary>返回该 Grain 注册的所有提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提醒器集合。</returns>
    public static Task<List<IGrainReminder>> GetReminders(this Grain grain, CancellationToken cancellationToken)
        => GetReminders(grain?.GrainContext, cancellationToken);

    /// <summary>
    /// 返回该 Grain 注册的所有提醒器的列表。
    /// </summary>
    /// <param name="grain">Grain 实例。</param>
    /// <returns>用于获取此 Grain 已注册提醒器列表的异步操作。</returns>
    public static Task<List<IGrainReminder>> GetReminders(this IGrainBase grain) => GetReminders(grain?.GrainContext, CancellationToken.None);

    /// <summary>返回该 Grain 注册的所有提醒器。</summary>
    /// <param name="grain">Grain 实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提醒器集合。</returns>
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
