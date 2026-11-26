using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 执行提醒数据基本储存的接口:
    /// Azure 表 4 SQL发展模拟器 grain和模拟执行。
    /// 定义为 grain 开发模拟器的界面 grain 案件。
    /// </summary>  
    public interface IReminderTable
    {
        /// <summary>
        /// 初始化了这个实例。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task StartAsync(CancellationToken cancellationToken = default)
#pragma warning disable CS0618 // Type or member is obsolete
            => Init();
#pragma warning restore CS0618 // Type or member is obsolete

        /// <summary>
        /// 初始化了这个实例。
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        [Obsolete("Implement and use StartAsync instead")]
        Task Init() => Task.CompletedTask;

        /// <summary>
        /// 读取与指定 grain相关的提醒表格条目。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <returns>与指定 grain.</returns>
        Task<ReminderTableData> ReadRows(GrainId grainId);

        /// <summary>
        /// 读取与指定 grain相关的提醒表格条目。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>与指定 grain.</returns>
        Task<ReminderTableData> ReadRows(GrainId grainId, CancellationToken cancellationToken) => ReadRows(grainId);

        /// <summary>
        /// 返回在范围( 开始、 结束) 中包含 <see cref="GrainId.GetUniformHashCode"/> 的所有行。
        /// 如果起始数大于或等于结束,则返回所有以散列较大起始数或小于或等于结束数的条目。
        /// </summary>
        /// <param name="begin">排他性下限约束</param>
        /// <param name="end">包含式上约束。</param>
        /// <returns>属于指定范围的提醒表条目。</returns>
        Task<ReminderTableData> ReadRows(uint begin, uint end);

        /// <summary>
        /// 返回 grain hash位于指定范围内的所有行。
        /// </summary>
        /// <param name="begin">排他性下限约束</param>
        /// <param name="end">包含式上约束。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>属于指定范围的提醒表条目。</returns>
        Task<ReminderTableData> ReadRows(uint begin, uint end, CancellationToken cancellationToken) => ReadRows(begin, end);

        /// <summary>
        /// 读取指定条目。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="reminderName">提醒函的名称。</param>
        /// <returns>提醒表格条目。</returns>
        Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName);

        /// <summary>
        /// 读取指定条目。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="reminderName">提醒函的名称。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>提醒表格条目。</returns>
        Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName, CancellationToken cancellationToken) => ReadRow(grainId, reminderName);

        /// <summary>
        /// 向指定条目上传。
        /// </summary>
        /// <param name="entry">条目。</param>
        /// <returns>新的 ETag 。</returns>
        Task<string?> UpsertRow(ReminderEntry entry);

        /// <summary>
        /// 向指定条目上传。
        /// </summary>
        /// <param name="entry">条目。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>新的 ETag 。</returns>
        Task<string?> UpsertRow(ReminderEntry entry, CancellationToken cancellationToken) => UpsertRow(entry);

        /// <summary>
        /// 删除表格中的一行。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// /// <param name="eTag">塔格号</param>
        /// <returns>真实的行 <paramref name="grainId"/> 和 <paramref name="reminderName"/> 存在, 并成功被删除, 否则是假</returns>
        Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag);

        /// <summary>
        /// 删除表格中的一行。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="reminderName">提醒的名称。</param>
        /// <param name="eTag">塔格号</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns><see langword="true"/> 如果删除了该行;否则, <see langword="false"/>。</returns>
        Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag, CancellationToken cancellationToken) => RemoveRow(grainId, reminderName, eTag);

        /// <summary>
        /// 清理桌子
        /// </summary>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task TestOnlyClearTable();

        /// <summary>
        /// 清理桌子
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task TestOnlyClearTable(CancellationToken cancellationToken) => TestOnlyClearTable();

        /// <summary>
        /// 停止提醒表。
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 用于基于 grain 的履行提醒表接口。
    /// </summary>
    internal interface IReminderTableGrain : IGrainWithIntegerKey
    {
        [Alias("EEEF6FCA")]
        Task<ReminderTableData> ReadRows(GrainId grainId, CancellationToken cancellationToken = default);

        [Alias("13558B55")]
        Task<ReminderTableData> ReadRows(uint begin, uint end, CancellationToken cancellationToken = default);

        [Alias("ECA791DE")]
        Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName, CancellationToken cancellationToken = default);

        [Alias("873299B5")]
        Task<string?> UpsertRow(ReminderEntry entry, CancellationToken cancellationToken = default);

        [Alias("FF391E0B")]
        Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag, CancellationToken cancellationToken = default);

        [Alias("8EBE0523")]
        Task TestOnlyClearTable(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 代表提醒表格条目集。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReminderTableData
    {
        /// <summary>
        /// 初始化 <see cref="ReminderTableData"/> 类的新实例。
        /// </summary>
        /// <param name="list">条目。</param>
        public ReminderTableData(IEnumerable<ReminderEntry> list)
        {
            Reminders = new List<ReminderEntry>(list);
        }

        /// <summary>
        /// 初始化 <see cref="ReminderTableData"/> 类的新实例。
        /// </summary>
        /// <param name="entry">条目。</param>
        public ReminderTableData(ReminderEntry entry)
        {
            Reminders = new[] { entry };
        }

        /// <summary>
        /// 初始化 <see cref="ReminderTableData"/> 类的新实例。
        /// </summary>
        public ReminderTableData()
        {
            Reminders = Array.Empty<ReminderEntry>();
        }

        /// <summary>
        /// 得到提醒。
        /// </summary>
        /// <value>提醒函。</value>
        [Id(0)]
        public IList<ReminderEntry> Reminders { get; private set; }

        /// <summary>
        /// 返回代表此实例的 <see cref="string" /> 。
        /// </summary>
        /// <returns>A <see cref="string" /> 代表此实例。</returns>
        public override string ToString() => $"[{Reminders.Count} reminders: {Utils.EnumerableToString(Reminders)}.";
    }

    /// <summary>
    /// 代表提醒表条目。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReminderEntry
    {
        /// <summary>
        /// 设置 grain 编号 ID grain 以便他创造教诲,而你对它加以教诲
        /// 主键与 <see cref="ReminderName"/>一起。
        /// </summary>
        [Id(0)]
        public GrainId GrainId { get; set; }

        /// <summary>
        /// 查找或设置提醒的名称。将提醒主要密钥与 
        /// <see cref="GrainId"/>.
        /// </summary>
        [Id(1)]
        public string ReminderName { get; set; } = null!;

        /// <summary>
        /// 获取或设定提醒第一次要滴答的时间
        /// </summary>
        [Id(2)]
        public DateTime StartAt { get; set; }

        /// <summary>
        /// 获取或设定提醒的时间段
        /// </summary>
        [Id(3)]
        public TimeSpan Period { get; set; }

        /// <summary>
        /// 设置或设置ETag。
        /// </summary>
        /// <value>塔格号</value>
        [Id(4)]
        public string? ETag { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"<GrainId={GrainId} ReminderName={ReminderName} Period={Period}>";

        /// <summary>
        /// 返回在此情况下代表数据的 <see cref="IGrainReminder"/> 。
        /// </summary>
        /// <returns>下 <see cref="IGrainReminder"/>.</returns>
        internal IGrainReminder ToIGrainReminder() => new ReminderData(GrainId, ReminderName, ETag!);
    }

    [Serializable, GenerateSerializer, Immutable]
    internal sealed class ReminderData : IGrainReminder
    {
        [Id(0)]
        public readonly GrainId GrainId;
        [Id(1)]
        public string ReminderName { get; }
        [Id(2)]
        public readonly string ETag;

        internal ReminderData(GrainId grainId, string reminderName, string eTag)
        {
            GrainId = grainId;
            ReminderName = reminderName;
            ETag = eTag;
        }

        public override string ToString() => $"<IOrleansReminder: GrainId={GrainId} ReminderName={ReminderName} ETag={ETag}>";
    }
}
