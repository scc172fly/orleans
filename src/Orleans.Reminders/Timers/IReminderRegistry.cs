using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Services;

namespace Orleans.Timers
{
    /// <summary>
    /// 提供管理提醒器的功能。
    /// </summary>
    public interface IReminderRegistry : IGrainServiceClient<IReminderService>
    {
        /// <summary>
        /// 为当前激活的 Grain 注册或更新具有指定名称的提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="dueTime">提醒器首次调用前的等待时间。<see cref="TimeSpan.Zero"/> 表示立即调度首次触发；负值、无限值或超出 <see cref="DateTime"/> 剩余范围的值均会被拒绝。</param>
        /// <param name="period">提醒器两次调用之间的时间间隔。该值必须至少为 <see cref="Orleans.Hosting.ReminderOptions.MinimumReminderPeriod"/>；更小的值和 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 均会被拒绝。</param>
        /// <returns>提醒器。</returns>
        /// <remarks>
        /// <paramref name="period"/> 没有用于单次提醒的特殊值。若要仅调度一次回调，请注册有效的正数周期，并在首次回调触发后注销提醒器。
        /// </remarks>
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId callingGrainId, string reminderName, TimeSpan dueTime, TimeSpan period);

        /// <summary>
        /// 为指定 Grain 注册或更新提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="dueTime">提醒器首次调用前的等待时间。</param>
        /// <param name="period">两次提醒器调用之间的时间间隔。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>提醒器。</returns>
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId callingGrainId, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
            => RegisterOrUpdateReminder(callingGrainId, reminderName, dueTime, period);

        /// <summary>
        /// 注销当前激活的 Grain 的提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="reminder">要注销的提醒器。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task UnregisterReminder(GrainId callingGrainId, IGrainReminder reminder);

        /// <summary>
        /// 注销指定 Grain 的提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="reminder">要注销的提醒器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task UnregisterReminder(GrainId callingGrainId, IGrainReminder reminder, CancellationToken cancellationToken)
            => UnregisterReminder(callingGrainId, reminder);

        /// <summary>
        /// 获取当前激活的 Grain 注册的具有指定名称的提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <returns>提醒器。</returns>
        Task<IGrainReminder?> GetReminder(GrainId callingGrainId, string reminderName);

        /// <summary>
        /// 获取指定 Grain 注册的提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>提醒器；如果未找到，则为 <see langword="null"/>。</returns>
        Task<IGrainReminder?> GetReminder(GrainId callingGrainId, string reminderName, CancellationToken cancellationToken)
            => GetReminder(callingGrainId, reminderName);

        /// <summary>
        /// 获取当前激活的 Grain 已注册的所有提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <returns>当前激活的 Grain 已注册的所有提醒器。</returns>
        Task<List<IGrainReminder>> GetReminders(GrainId callingGrainId);

        /// <summary>
        /// 获取指定 Grain 已注册的所有提醒器。
        /// </summary>
        /// <param name="callingGrainId">当前正在执行的 Grain 的 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>指定 Grain 已注册的所有提醒器。</returns>
        Task<List<IGrainReminder>> GetReminders(GrainId callingGrainId, CancellationToken cancellationToken)
            => GetReminders(callingGrainId);
    }
}
