using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Services;

namespace Orleans.Timers
{
    /// <summary>
    /// 管理催复通知的职能。
    /// </summary>
    public interface IReminderRegistry : IGrainServiceClient<IReminderService>
    {
        /// <summary>
        /// 登记或更新当前活动的 grain的提醒通知,并注明指定的名称。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain</param>的代号
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="dueTime">最初援引提醒前要延迟的时间。 <see cref="TimeSpan.Zero"/> 值意味着第一个秒被立即排期;负值、无限值或超过剩余 <see cref="DateTime"/> 范围的数值被否决。</param>
        /// <param name="period">提醒的引用间隔。 值必须至少为 <see cref="Orleans.Hosting.ReminderOptions.MinimumReminderPeriod"/>; 小数值和 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 被否决。</param>
        /// 提醒<returns>。</returns>
        /// <remarks>
        /// <paramref name="period"/>没有特殊的一次性提醒值。 要排入一次回调, 请登记一个有效的正时段并在第一次回调火灾后取消提醒 。
        /// </remarks>
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId callingGrainId, string reminderName, TimeSpan dueTime, TimeSpan period);

        /// <summary>
        /// 登记或更新关于指定 grain的提醒通知。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain的代号。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="dueTime">最初援引催复通知之前的延迟时间。</param>
        /// <param name="period">提醒的援引间隔时间。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// 提醒<returns>。</returns>
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId callingGrainId, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
            => RegisterOrUpdateReminder(callingGrainId, reminderName, dueTime, period);

        /// <summary>
        /// 向当前活动者发出提醒 grain.
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain</param>的代号
        /// <param name="reminder">提醒要不登记</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task UnregisterReminder(GrainId callingGrainId, IGrainReminder reminder);

        /// <summary>
        /// 将指定的 grain中的提醒通知不登记。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain的代号。</param>
        /// <param name="reminder">提醒要不登记</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task UnregisterReminder(GrainId callingGrainId, IGrainReminder reminder, CancellationToken cancellationToken)
            => UnregisterReminder(callingGrainId, reminder);

        /// <summary>
        /// 获取提醒,使其使用当前活动 grain注册的指定名称。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain</param>的代号
        /// <param name="reminderName">提醒的名称。</param>
        /// 提醒<returns>。</returns>
        Task<IGrainReminder?> GetReminder(GrainId callingGrainId, string reminderName);

        /// <summary>
        /// 获取已注册到指定 grain的提醒 。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain的代号。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>或 <see langword="null"/> 如果找不到的话</returns>
        Task<IGrainReminder?> GetReminder(GrainId callingGrainId, string reminderName, CancellationToken cancellationToken)
            => GetReminder(callingGrainId, reminderName);

        /// <summary>
        /// 获取当前注册到活动 grain的所有提醒 。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain</param>的代号
        /// <returns>所有当前登记到活动 grain.</returns>
        Task<List<IGrainReminder>> GetReminders(GrainId callingGrainId);

        /// <summary>
        /// 将所有提醒通知登记到指定的 grain。
        /// </summary>
        /// <param name="callingGrainId">当前执行的 grain的代号。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>向指定 grain登记的所有提醒通知。</returns>
        Task<List<IGrainReminder>> GetReminders(GrainId callingGrainId, CancellationToken cancellationToken)
            => GetReminders(callingGrainId);
    }
}
