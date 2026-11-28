using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// <see cref="IReminderTable"/> 契约的确定性、强一致性、内存参考实现，
/// 独立于所有生产环境提醒器提供程序编写。
/// </summary>
/// <remarks>
/// <para>
/// 此参考实现用于在将符合性测试套件应用于实际提供程序之前，
/// 先使用已知正确的实现验证测试套件。它不是生产环境提供程序的包装器，而是维护自身的存储、
/// 自身单调递增的 ETag 序列，以及直接依据文档契约实现的范围判断逻辑。
/// </para>
/// <para>内部状态观测：<see cref="Snapshot"/>、<see cref="Operations"/>、<see cref="OperationCount"/>。</para>
/// <para>
/// 控制项：<see cref="SetAvailable"/>（存储中断）、<see cref="InjectFailure"/>（暂时性故障）、
/// <see cref="BlockNext"/>（确定性同步屏障）、<see cref="FreezeReads"/>（过期快照），以及
/// 对 <see cref="StartAsync"/> 和 <see cref="StopAsync"/> 的取消观测。
/// </para>
/// <para>
/// 在导致不变量被破坏的操作处检测违规，并将其报告为
/// <see cref="ReminderConformanceException"/>，其中携带结构化的 <see cref="ReminderFailureReport"/>。
/// </para>
/// </remarks>
public sealed class IdealizedReminderTable : IReminderTable
{
    private readonly object _gate = new();
    private readonly Dictionary<(GrainId GrainId, string ReminderName), ReminderTableRecord> _records = new();
    private readonly HashSet<string> _issuedETags = new(StringComparer.Ordinal);
    private readonly List<ReminderTableOperation> _operations = [];
    private readonly List<ReminderTableOperationGate> _gates = [];
    private readonly Dictionary<ReminderTableOperationKind, Queue<Func<Exception>>> _injectedFailures = [];

    private Dictionary<(GrainId GrainId, string ReminderName), ReminderTableRecord>? _frozenReads;
    private long _etagCounter;
    private long _sequence;
    private long _versionCounter;
    private bool _available = true;
    private bool _started;

    /// <summary>
    /// 初始化 <see cref="IdealizedReminderTable"/> 类的新实例。
    /// </summary>
    /// <param name="name">用于在诊断中标识此实例的名称。</param>
    public IdealizedReminderTable(string name = "IdealizedReminderTable")
    {
        Name = string.IsNullOrWhiteSpace(name) ? "IdealizedReminderTable" : name;
    }

    /// <summary>
    /// 获取用于在诊断中标识此实例的名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取一个值，指示 <see cref="StartAsync"/> 的完成时间是否晚于 <see cref="StopAsync"/>。
    /// </summary>
    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    /// <summary>
    /// 获取一个值，指示此表当前是否正在处理请求。
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    /// <summary>
    /// 返回所有持久化记录按确定性顺序排列的快照。
    /// </summary>
    /// <returns>按均匀哈希码、Grain 标识和提醒器名称排序的记录。</returns>
    public IReadOnlyList<ReminderTableRecord> Snapshot()
    {
        lock (_gate)
        {
            return OrderRecords(_records.Values).ToList();
        }
    }

    /// <summary>
    /// 尝试读取单条持久化记录。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <returns>该记录；不存在时返回 <see langword="null"/>。</returns>
    public ReminderTableRecord? Find(GrainId grainId, string reminderName)
    {
        lock (_gate)
        {
            return _records.TryGetValue((grainId, reminderName), out var record) ? record : null;
        }
    }

    /// <summary>
    /// 返回此表观测到的所有操作的有序日志。
    /// </summary>
    public IReadOnlyList<ReminderTableOperation> Operations
    {
        get
        {
            lock (_gate)
            {
                return _operations.ToList();
            }
        }
    }

    /// <summary>
    /// 统计目前为止观测到的指定类型操作的数量。
    /// </summary>
    /// <param name="kind">操作类型。</param>
    /// <returns>数量。</returns>
    public int OperationCount(ReminderTableOperationKind kind)
    {
        lock (_gate)
        {
            return _operations.Count(operation => operation.Kind == kind);
        }
    }

    /// <summary>
    /// 清除记录的操作日志，不影响持久化状态。
    /// </summary>
    public void ClearOperations()
    {
        lock (_gate)
        {
            _operations.Clear();
        }
    }

    /// <summary>
    /// 将表标记为可用或不可用，以模拟存储中断及恢复。
    /// </summary>
    /// <param name="available">此表是否应处理请求。</param>
    public void SetAvailable(bool available)
    {
        lock (_gate)
        {
            _available = available;
        }
    }

    /// <summary>
    /// 将针对指定操作类型注入的一个或多个故障加入队列。
    /// </summary>
    /// <param name="kind">要使其失败的操作类型。</param>
    /// <param name="count">要连续失败的次数。</param>
    /// <param name="exceptionFactory">生成异常的工厂；使用 <see langword="null"/> 表示采用默认工厂。</param>
    public void InjectFailure(ReminderTableOperationKind kind, int count = 1, Func<Exception>? exceptionFactory = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var factory = exceptionFactory ?? (() => new ReminderTableUnavailableException($"{Name}: injected failure for {kind}."));
        lock (_gate)
        {
            if (!_injectedFailures.TryGetValue(kind, out var queue))
            {
                queue = new Queue<Func<Exception>>();
                _injectedFailures[kind] = queue;
            }

            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(factory);
            }
        }
    }

    /// <summary>
    /// 移除队列中的全部注入故障。
    /// </summary>
    public void ClearInjectedFailures()
    {
        lock (_gate)
        {
            _injectedFailures.Clear();
        }
    }

    /// <summary>
    /// 阻塞下一次指定类型的操作，直至返回的门控对象被释放。
    /// </summary>
    /// <param name="kind">要阻塞的操作类型。</param>
    /// <returns>门控对象。</returns>
    public ReminderTableOperationGate BlockNext(ReminderTableOperationKind kind)
    {
        var gate = new ReminderTableOperationGate(this, kind);
        lock (_gate)
        {
            _gates.Add(gate);
        }

        return gate;
    }

    /// <summary>
    /// 将读取冻结在当前持久化状态，使后续读取观测到过期快照，同时写入
    /// 仍继续生效。
    /// </summary>
    /// <returns>释放时恢复实时读取的句柄。</returns>
    public IDisposable FreezeReads()
    {
        lock (_gate)
        {
            _frozenReads = new Dictionary<(GrainId, string), ReminderTableRecord>(_records);
        }

        return new ReadFreeze(this);
    }

    internal void UnfreezeReads()
    {
        lock (_gate)
        {
            _frozenReads = null;
        }
    }

    internal void RemoveGate(ReminderTableOperationGate gate)
    {
        lock (_gate)
        {
            _gates.Remove(gate);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // IReminderTable
    // ---------------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await BeforeOperationAsync(ReminderTableOperationKind.Start, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _started = true;
            Record(ReminderTableOperationKind.Start, null, null, null, null, null, null, true, 0, null);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await BeforeOperationAsync(ReminderTableOperationKind.Stop, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _started = false;
            Record(ReminderTableOperationKind.Stop, null, null, null, null, null, null, true, 0, null);
        }
    }

    /// <inheritdoc />
    public async Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
    {
        ArgumentNullException.ThrowIfNull(reminderName);
        await BeforeOperationAsync(ReminderTableOperationKind.ReadRow, grainId, reminderName);

        lock (_gate)
        {
            var source = _frozenReads ?? _records;
            var found = source.TryGetValue((grainId, reminderName), out var record);
            Record(ReminderTableOperationKind.ReadRow, grainId, reminderName, null, null, null, record?.ETag, true, found ? 1 : 0, null);
            return found ? ToEntry(record!) : null;
        }
    }

    /// <inheritdoc />
    public async Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        await BeforeOperationAsync(ReminderTableOperationKind.ReadGrainRows, grainId);

        lock (_gate)
        {
            var source = _frozenReads ?? _records;
            var matches = OrderRecords(source.Values.Where(record => record.GrainId.Equals(grainId))).Select(ToEntry).ToList();
            Record(ReminderTableOperationKind.ReadGrainRows, grainId, null, null, null, null, null, true, matches.Count, null);
            return new ReminderTableData(matches);
        }
    }

    /// <inheritdoc />
    public async Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        await BeforeOperationAsync(ReminderTableOperationKind.ReadRange, begin: begin, end: end);

        lock (_gate)
        {
            var source = _frozenReads ?? _records;
            var matches = OrderRecords(source.Values.Where(record => InRange(record.UniformHashCode, begin, end))).Select(ToEntry).ToList();
            Record(ReminderTableOperationKind.ReadRange, null, null, begin, end, null, null, true, matches.Count, null);
            return new ReminderTableData(matches);
        }
    }

    /// <inheritdoc />
    public async Task<string?> UpsertRow(ReminderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.ReminderName);
        await BeforeOperationAsync(ReminderTableOperationKind.UpsertRow, entry.GrainId, entry.ReminderName);

        lock (_gate)
        {
            var key = (entry.GrainId, entry.ReminderName);
            _records.TryGetValue(key, out var existing);

            var etag = NextETag(entry.GrainId, entry.ReminderName);
            var record = new ReminderTableRecord(
                entry.GrainId,
                entry.ReminderName,
                entry.StartAt,
                entry.Period,
                etag,
                existing?.ETag,
                ++_versionCounter);

            CheckRecordInvariants(ReminderTableOperationKind.UpsertRow, key, record, existing);
            _records[key] = record;
            Record(ReminderTableOperationKind.UpsertRow, entry.GrainId, entry.ReminderName, null, null, entry.ETag, etag, true, 1, null);
            return etag;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        ArgumentNullException.ThrowIfNull(reminderName);
        await BeforeOperationAsync(ReminderTableOperationKind.RemoveRow, grainId, reminderName);

        lock (_gate)
        {
            var key = (grainId, reminderName);
            var removed = _records.TryGetValue(key, out var existing)
                && existing is not null
                && string.Equals(existing.ETag, eTag, StringComparison.Ordinal);

            if (removed)
            {
                _records.Remove(key);
            }

            Record(ReminderTableOperationKind.RemoveRow, grainId, reminderName, null, null, eTag, existing?.ETag, removed, removed ? 1 : 0, null);
            return removed;
        }
    }

    /// <inheritdoc />
    public async Task TestOnlyClearTable()
    {
        await BeforeOperationAsync(ReminderTableOperationKind.ClearTable);

        lock (_gate)
        {
            var count = _records.Count;
            _records.Clear();
            _frozenReads = null;
            Record(ReminderTableOperationKind.ClearTable, null, null, null, null, null, null, true, count, null);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // 内部实现
    // ---------------------------------------------------------------------------------------------------------

    private async Task BeforeOperationAsync(
        ReminderTableOperationKind kind,
        GrainId? grainId = null,
        string? reminderName = null,
        uint? begin = null,
        uint? end = null,
        CancellationToken cancellationToken = default)
    {
        ReminderTableOperationGate? gate = null;
        lock (_gate)
        {
            for (var i = 0; i < _gates.Count; i++)
            {
                if (_gates[i].Kind == kind)
                {
                    gate = _gates[i];
                    _gates.RemoveAt(i);
                    break;
                }
            }
        }

        if (gate is not null)
        {
            gate.MarkBlocked();
            await gate.WaitForReleaseAsync(cancellationToken);
        }

        Func<Exception>? failure = null;
        bool available;
        lock (_gate)
        {
            available = _available;
            if (_injectedFailures.TryGetValue(kind, out var queue) && queue.Count > 0)
            {
                failure = queue.Dequeue();
            }
        }

        if (!available)
        {
            lock (_gate)
            {
                Record(kind, grainId, reminderName, begin, end, null, null, false, 0, nameof(ReminderTableUnavailableException));
            }

            throw new ReminderTableUnavailableException($"{Name}: the reminder table is unavailable (simulated storage outage) for {kind}.");
        }

        if (failure is not null)
        {
            var exception = failure();
            lock (_gate)
            {
                Record(kind, grainId, reminderName, begin, end, null, null, false, 0, exception.GetType().Name);
            }

            throw exception;
        }
    }

    private string NextETag(GrainId grainId, string reminderName)
    {
        var etag = $"etag-{(++_etagCounter).ToString("D6", CultureInfo.InvariantCulture)}";
        if (!_issuedETags.Add(etag))
        {
            ReminderFailureReport.Create(Name, nameof(IdealizedReminderTable), "UpsertRow")
                .WithIdentity(grainId, reminderName)
                .WithExpected("every issued ETag to be unique for the lifetime of the table")
                .WithObserved($"the ETag '{etag}' was issued twice")
                .WithETags(etag)
                .Throw();
        }

        return etag;
    }

    private void CheckRecordInvariants(
        ReminderTableOperationKind kind,
        (GrainId GrainId, string ReminderName) key,
        ReminderTableRecord record,
        ReminderTableRecord? existing)
    {
        if (!record.GrainId.Equals(key.GrainId) || !string.Equals(record.ReminderName, key.ReminderName, StringComparison.Ordinal))
        {
            ReminderFailureReport.Create(Name, nameof(CheckRecordInvariants), kind.ToString())
                .WithIdentity(key.GrainId, key.ReminderName)
                .WithExpected($"the stored record identity to equal its key ({key.GrainId}, '{key.ReminderName}')")
                .WithObserved($"({record.GrainId}, '{record.ReminderName}')")
                .Throw();
        }

        if (existing is not null && record.Version <= existing.Version)
        {
            ReminderFailureReport.Create(Name, nameof(CheckRecordInvariants), kind.ToString())
                .WithIdentity(key.GrainId, key.ReminderName)
                .WithExpected($"the record version to increase beyond {existing.Version.ToString(CultureInfo.InvariantCulture)}")
                .WithObserved($"version {record.Version.ToString(CultureInfo.InvariantCulture)}")
                .WithETags(record.ETag, existing.ETag)
                .Throw();
        }

        if (existing is not null && string.Equals(existing.ETag, record.ETag, StringComparison.Ordinal))
        {
            ReminderFailureReport.Create(Name, nameof(CheckRecordInvariants), kind.ToString())
                .WithIdentity(key.GrainId, key.ReminderName)
                .WithExpected("a write to replace the previous ETag")
                .WithObserved($"the ETag '{record.ETag}' was reused")
                .WithETags(record.ETag, existing.ETag)
                .Throw();
        }
    }

    private void Record(
        ReminderTableOperationKind kind,
        GrainId? grainId,
        string? reminderName,
        uint? begin,
        uint? end,
        string? suppliedETag,
        string? resultETag,
        bool succeeded,
        int resultCount,
        string? failure)
        => _operations.Add(new ReminderTableOperation(++_sequence, kind, grainId, reminderName, begin, end, suppliedETag, resultETag, succeeded, resultCount, failure));

    private static IEnumerable<ReminderTableRecord> OrderRecords(IEnumerable<ReminderTableRecord> records)
        => records
            .OrderBy(record => record.UniformHashCode)
            .ThenBy(record => record.GrainId.ToString(), StringComparer.Ordinal)
            .ThenBy(record => record.ReminderName, StringComparer.Ordinal);

    private static ReminderEntry ToEntry(ReminderTableRecord record) => new()
    {
        GrainId = record.GrainId,
        ReminderName = record.ReminderName,
        StartAt = record.StartAt,
        Period = record.Period,
        ETag = record.ETag
    };

    /// <summary>
    /// 判断均匀哈希码是否位于范围 (<paramref name="begin"/>, <paramref name="end"/>] 内。
    /// </summary>
    /// <param name="hash">均匀哈希码。</param>
    /// <param name="begin">下界（不含该端点）。</param>
    /// <param name="end">上界（包含该端点）。</param>
    /// <returns><see langword="true"/> 表示哈希码位于范围内。</returns>
    /// <remarks>当 <paramref name="begin"/> 大于或等于 <paramref name="end"/> 时，范围跨越
    /// 零点，并匹配大于 <paramref name="begin"/> 或小于等于 <paramref name="end"/> 的哈希码。</remarks>
    public static bool InRange(uint hash, uint begin, uint end)
        => begin < end ? hash > begin && hash <= end : hash > begin || hash <= end;

    private sealed class ReadFreeze(IdealizedReminderTable owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.UnfreezeReads();
            }
        }
    }
}
