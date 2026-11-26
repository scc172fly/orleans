using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Services;

namespace Orleans
{
    /// <summary>
    /// 管理催复通知的职能。
    /// </summary>
    public interface IReminderService : IGrainService
    {
        /// <summary>
        /// 开始服务。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("Start")]
        Task Start();

        /// <summary>
        /// 开始服务。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("5CF78F8A")]
        Task Start(CancellationToken cancellationToken) => Start();

        /// <summary>
        /// 停止服务。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("Stop")]
        Task Stop();

        /// <summary>
        /// 停止服务。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("DCFCA00D")]
        Task Stop(CancellationToken cancellationToken) => Stop();

        /// <summary>
        /// 登记新的催复通知或更新现有的催复通知。
        /// </summary>
        /// <param name="grainId">提及 grain 以该提醒函的名义登记或更新。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="dueTime">最初发出提醒通知前的延迟时间。</param>
        /// <param name="period">提醒的援引间隔时间。</param>
        /// 提醒<returns>。</returns>
        [Alias("RegisterOrUpdateReminder")]
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period);

        /// <summary>
        /// 登记新的催复通知或更新现有的催复通知。
        /// </summary>
        /// <param name="grainId">提及 grain 以该提醒函的名义登记或更新。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="dueTime">最初发出提醒通知前的延迟时间。</param>
        /// <param name="period">提醒的援引间隔时间。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// 提醒<returns>。</returns>
        [Alias("1281C86D")]
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
            => RegisterOrUpdateReminder(grainId, reminderName, dueTime, period);

        /// <summary>
        /// 取消指定提醒的登记 。
        /// </summary>
        /// 提醒<param name="reminder">。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("UnregisterReminder")]
        Task UnregisterReminder(IGrainReminder reminder);

        /// <summary>
        /// 取消指定提醒的登记 。
        /// </summary>
        /// 提醒<param name="reminder">。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        [Alias("A7AF84A8")]
        Task UnregisterReminder(IGrainReminder reminder, CancellationToken cancellationToken) => UnregisterReminder(reminder);

        /// <summary>
        /// 将提醒通知登记到指定的名称为提供名称的 grain 。
        /// </summary>
        /// <param name="grainId">提及 grain 那是已记录在《古兰经》上的。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// 提醒<returns>。</returns>
        [Alias("GetReminder")]
        Task<IGrainReminder?> GetReminder(GrainId grainId, string reminderName);

        /// <summary>
        /// 将提醒通知登记到指定的名称为提供名称的 grain 。
        /// </summary>
        /// <param name="grainId">提及 grain 那是已记录在《古兰经》上的。</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// 提醒<returns>。</returns>
        [Alias("AC622EEB")]
        Task<IGrainReminder?> GetReminder(GrainId grainId, string reminderName, CancellationToken cancellationToken)
            => GetReminder(grainId, reminderName);

        /// <summary>
        /// 获取所有特定 grain的登记提醒。
        /// </summary>
        /// <param name="grainId">提及 grain.</param>
        /// <returns>所指定机构所有已登记提醒通知的清单 grain.</returns>
        [Alias("GetReminders")]
        Task<List<IGrainReminder>> GetReminders(GrainId grainId);

        /// <summary>
        /// 获取所有特定 grain的登记提醒。
        /// </summary>
        /// <param name="grainId">提及 grain.</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>所指定机构所有已登记提醒通知的清单 grain.</returns>
        [Alias("419EB51E")]
        Task<List<IGrainReminder>> GetReminders(GrainId grainId, CancellationToken cancellationToken) => GetReminders(grainId);
    }
}
