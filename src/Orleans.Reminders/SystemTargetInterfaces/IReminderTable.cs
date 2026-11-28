using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 提醒器数据底层存储实现的接口，包括：
    /// Azure Table、SQL、开发环境模拟器 Grain 以及模拟实现。
    /// 在使用开发环境模拟器 Grain 的情况下，定义为 Grain 接口。
    /// </summary>  
    public interface IReminderTable
    {
        /// <summary>
        /// 初始化此实例。
        /// </summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task StartAsync(CancellationToken cancellationToken = default)
#pragma warning disable CS0618 // Type or member is obsolete
            => Init();
#pragma warning restore CS0618 // Type or member is obsolete

        /// <summary>
        /// 初始化此实例。
        /// </summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        [Obsolete("Implement and use StartAsync instead")]
        Task Init() => Task.CompletedTask;

        /// <summary>
        /// 读取与指定 Grain 关联的提醒器表条目。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <returns>与指定 Grain 关联的提醒器表条目。</returns>
        Task<ReminderTableData> ReadRows(GrainId grainId);

        /// <summary>
        /// 读取与指定 Grain 关联的提醒器表条目。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>与指定 Grain 关联的提醒器表条目。</returns>
        Task<ReminderTableData> ReadRows(GrainId grainId, CancellationToken cancellationToken) => ReadRows(grainId);

        /// <summary>
        /// 返回 <see cref="GrainId.GetUniformHashCode"/> 位于范围 (begin, end] 内的所有行。
        /// 如果 begin 大于或等于 end，则返回哈希值大于 begin 或小于等于 end 的所有条目。
        /// </summary>
        /// <param name="begin">不包含的下界。</param>
        /// <param name="end">包含的上界。</param>
        /// <returns>位于指定范围内的提醒器表条目。</returns>
        Task<ReminderTableData> ReadRows(uint begin, uint end);

        /// <summary>
        /// 返回 Grain 哈希值位于指定范围内的所有行。
        /// </summary>
        /// <param name="begin">不包含的下界。</param>
        /// <param name="end">包含的上界。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>位于指定范围内的提醒器表条目。</returns>
        Task<ReminderTableData> ReadRows(uint begin, uint end, CancellationToken cancellationToken) => ReadRows(begin, end);

        /// <summary>
        /// 读取指定条目。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <returns>提醒器表条目。</returns>
        Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName);

        /// <summary>
        /// 读取指定条目。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>提醒器表条目。</returns>
        Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName, CancellationToken cancellationToken) => ReadRow(grainId, reminderName);

        /// <summary>
        /// 插入或更新指定条目。
        /// </summary>
        /// <param name="entry">条目。</param>
        /// <returns>该行的新 ETag。</returns>
        Task<string?> UpsertRow(ReminderEntry entry);

        /// <summary>
        /// 插入或更新指定条目。
        /// </summary>
        /// <param name="entry">条目。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>该行的新 ETag。</returns>
        Task<string?> UpsertRow(ReminderEntry entry, CancellationToken cancellationToken) => UpsertRow(entry);

        /// <summary>
        /// 从表中移除一行。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// /// <param name="eTag">ETag。</param>
        /// <returns>如果具有 <paramref name="grainId"/> 和 <paramref name="reminderName"/> 的行存在并成功移除，则为 true；否则为 false。</returns>
        Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag);

        /// <summary>
        /// 从表中移除一行。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="reminderName">提醒器名称。</param>
        /// <param name="eTag">ETag。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns><see langword="true"/>，如果该行已移除；否则为 <see langword="false"/>。</returns>
        Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag, CancellationToken cancellationToken) => RemoveRow(grainId, reminderName, eTag);

        /// <summary>
        /// 清空表。
        /// </summary>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task TestOnlyClearTable();

        /// <summary>
        /// 清空表。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task TestOnlyClearTable(CancellationToken cancellationToken) => TestOnlyClearTable();

        /// <summary>
        /// 停止提醒器表。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 基于 Grain 实现的提醒器表接口。
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
    /// 表示提醒器表条目的集合。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReminderTableData
    {
        /// <summary>
        /// 初始化 <see cref="ReminderTableData"/> 类的新实例。
        /// </summary>
        /// <param name="list">条目集合。</param>
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
        /// 获取提醒器集合。
        /// </summary>
        /// <value>提醒器集合。</value>
        [Id(0)]
        public IList<ReminderEntry> Reminders { get; private set; }

        /// <summary>
        /// 返回表示此实例的 <see cref="string" />。
        /// </summary>
        /// <returns>表示此实例的 <see cref="string" />。</returns>
        public override string ToString() => $"[{Reminders.Count} reminders: {Utils.EnumerableToString(Reminders)}.";
    }

    /// <summary>
    /// 表示一个提醒器表条目。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ReminderEntry
    {
        /// <summary>
        /// 获取或设置创建提醒器的 Grain 的 ID。此 ID 与
        /// <see cref="ReminderName"/> 共同构成提醒器的主键。
        /// </summary>
        [Id(0)]
        public GrainId GrainId { get; set; }

        /// <summary>
        /// 获取或设置提醒器的名称。此名称与下列值共同构成提醒器主键：
        /// <see cref="GrainId"/>.
        /// </summary>
        [Id(1)]
        public string ReminderName { get; set; } = null!;

        /// <summary>
        /// 获取或设置提醒器应首次触发的时间。
        /// </summary>
        [Id(2)]
        public DateTime StartAt { get; set; }

        /// <summary>
        /// 获取或设置提醒器的触发周期。
        /// </summary>
        [Id(3)]
        public TimeSpan Period { get; set; }

        /// <summary>
        /// 获取或设置 ETag。
        /// </summary>
        /// <value>ETag。</value>
        [Id(4)]
        public string? ETag { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"<GrainId={GrainId} ReminderName={ReminderName} Period={Period}>";

        /// <summary>
        /// 返回表示此实例中数据的 <see cref="IGrainReminder"/>。
        /// </summary>
        /// <returns><see cref="IGrainReminder"/>。</returns>
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
