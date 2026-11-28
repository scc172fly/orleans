using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Services;

namespace Orleans
{
    /// <summary>
    /// 提供管理提醒器的功能。
    /// </summary>
    public interface IReminderService : IGrainService
    {
        /// <summary>
        /// 启动服务。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("Start")]
        Task Start();

        /// <summary>
        /// 启动服务。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("5CF78F8A")]
        Task Start(CancellationToken cancellationToken) => Start();

        /// <summary>
        /// 停止服务。
        /// </summary>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("Stop")]
        Task Stop();

        /// <summary>
        /// 停止服务。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("DCFCA00D")]
        Task Stop(CancellationToken cancellationToken) => Stop();

        /// <summary>
        /// 注册新的提醒器或更新现有提醒器。
        /// </summary>
        /// <param name="grainId">所代为注册或更新提醒器的 Grain 的引用。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="dueTime">提醒器首次触发前的等待时间。</param>
        /// <param name="period">两次提醒器调用之间的时间间隔。</param>
        /// <returns>提醒器。</returns>
        [Alias("RegisterOrUpdateReminder")]
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period);

        /// <summary>
        /// 注册新的提醒器或更新现有提醒器。
        /// </summary>
        /// <param name="grainId">所代为注册或更新提醒器的 Grain 的引用。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="dueTime">提醒器首次触发前的等待时间。</param>
        /// <param name="period">两次提醒器调用之间的时间间隔。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>提醒器。</returns>
        [Alias("1281C86D")]
        Task<IGrainReminder> RegisterOrUpdateReminder(GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period, CancellationToken cancellationToken)
            => RegisterOrUpdateReminder(grainId, reminderName, dueTime, period);

        /// <summary>
        /// 注销指定提醒器。
        /// </summary>
        /// <param name="reminder">提醒器。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("UnregisterReminder")]
        Task UnregisterReminder(IGrainReminder reminder);

        /// <summary>
        /// 注销指定提醒器。
        /// </summary>
        /// <param name="reminder">提醒器。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        [Alias("A7AF84A8")]
        Task UnregisterReminder(IGrainReminder reminder, CancellationToken cancellationToken) => UnregisterReminder(reminder);

        /// <summary>
        /// 获取指定 Grain 注册的具有给定名称的提醒器。
        /// </summary>
        /// <param name="grainId">注册了该提醒器的 Grain 的引用。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <returns>提醒器。</returns>
        [Alias("GetReminder")]
        Task<IGrainReminder?> GetReminder(GrainId grainId, string reminderName);

        /// <summary>
        /// 获取指定 Grain 注册的具有给定名称的提醒器。
        /// </summary>
        /// <param name="grainId">注册了该提醒器的 Grain 的引用。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>提醒器。</returns>
        [Alias("AC622EEB")]
        Task<IGrainReminder?> GetReminder(GrainId grainId, string reminderName, CancellationToken cancellationToken)
            => GetReminder(grainId, reminderName);

        /// <summary>
        /// 获取指定 Grain 注册的所有提醒器。
        /// </summary>
        /// <param name="grainId">Grain 的引用。</param>
        /// <returns>指定 Grain 已注册的所有提醒器的列表。</returns>
        [Alias("GetReminders")]
        Task<List<IGrainReminder>> GetReminders(GrainId grainId);

        /// <summary>
        /// 获取指定 Grain 注册的所有提醒器。
        /// </summary>
        /// <param name="grainId">Grain 的引用。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>指定 Grain 已注册的所有提醒器的列表。</returns>
        [Alias("419EB51E")]
        Task<List<IGrainReminder>> GetReminders(GrainId grainId, CancellationToken cancellationToken) => GetReminders(grainId);
    }
}
