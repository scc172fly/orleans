using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 确定性、强烈一致和初步参照 <see cref="IReminderTable"/>
/// 合同,与每个生产提醒提供者独立订立。
/// </summary>
/// <remarks>
/// <para>
/// 甲骨文的存在,以便能对照已知的正确执行程序验证符合性套件
/// 指出的是真正的供应商,它不是生产供应商的包装纸:它维持自己的储存、其销售和销售。
/// 自己的单体ETag序列和其本身的射程评价直接来自有文件证明的合同。
/// </para>
/// <para>内审: <see cref="Snapshot"/>, <see cref="Operations"/>, <see cref="OperationCount"/>.</para>
/// <para>
/// 控制: <see cref="SetAvailable"/> (储存中断)、 <see cref="InjectFailure"/> (中度故障),
/// <see cref="BlockNext"/> (确定同步障碍)、 <see cref="FreezeReads"/> (短片)和
/// 对 <see cref="StartAsync"/> 和 <see cref="StopAsync"/>的注销观测。
/// </para>
/// <para>
/// 导致这些侵犯行为的行动发现各种侵犯行为,报告为:
/// <see cref="ReminderConformanceException"/> 带有结构化 <see cref="ReminderFailureReport"/>。
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
    /// <param name="name">用于识别诊断中此实例的名称。</param>
    public IdealizedReminderTable(string name = "IdealizedReminderTable")
    {
        Name = string.IsNullOrWhiteSpace(name) ? "IdealizedReminderTable" : name;
    }

    /// <summary>
    /// 获取用于在诊断中识别此实例的名称 。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获得一个值,以显示 <see cref="StartAsync"/> 是否最近完成过 <see cref="StopAsync"/>。
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
    /// 获得一个值,以显示表格是否正在满足请求。
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
    /// 返回的每个持久记录都有一个定序快照。
    /// </summary>
    /// <returns>统一散列码下的命令记录 grain 和提醒名称。</returns>
    public IReadOnlyList<ReminderTableRecord> Snapshot()
    {
        lock (_gate)
        {
            return OrderRecords(_records.Values).ToList();
        }
    }

    /// <summary>
    /// 试图阅读一份单一的持久记录。
    /// </summary>
    /// <param name="grainId"> grain 标识符。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <returns>(a) 记录,或 <see langword="null"/> 不存在时。</returns>
    public ReminderTableRecord? Find(GrainId grainId, string reminderName)
    {
        lock (_gate)
        {
            return _records.TryGetValue((grainId, reminderName), out var record) ? record : null;
        }
    }

    /// <summary>
    /// 返回本表格所观察到的每个操作的顺序日志。
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
    /// 计及迄今观察到的指定类型的操作。
    /// </summary>
    /// <param name="kind">操作类型。</param>
    /// <returns>计数。</returns>
    public int OperationCount(ReminderTableOperationKind kind)
    {
        lock (_gate)
        {
            return _operations.Count(operation => operation.Kind == kind);
        }
    }

    /// <summary>
    /// 清除记录中的操作日志,不影响耐久状态。
    /// </summary>
    public void ClearOperations()
    {
        lock (_gate)
        {
            _operations.Clear();
        }
    }

    /// <summary>
    /// 标出可用或无法使用的表格,模拟储存中断和回收。
    /// </summary>
    /// <param name="available">表格是否满足请求。</param>
    public void SetAvailable(bool available)
    {
        lock (_gate)
        {
            _available = available;
        }
    }

    /// <summary>
    /// 将指定操作种类的一个或多个注射失败。
    /// </summary>
    /// <param name="kind">操作类型失败。</param>
    /// <param name="count">连续发生失败的次数。</param>
    /// <param name="exceptionFactory">生产例外情形或 <see langword="null"/> 的工厂,因违约。</param>
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
    /// 删除每个队列的注射失败 。
    /// </summary>
    public void ClearInjectedFailures()
    {
        lock (_gate)
        {
            _injectedFailures.Clear();
        }
    }

    /// <summary>
    /// 屏蔽指定类型的下一次操作,直到返回的大门被释放。
    /// </summary>
    /// <param name="kind">封堵的操作类型。</param>
    /// <returns>大门</returns>
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
    /// 冻结规定与目前的耐久状态相反,
    /// 继续适用。
    /// </summary>
    /// <returns>处理时恢复活的控点读作。</returns>
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
    // 内部内部
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
    /// 确定统一的散列代码是否属于范围(<paramref name="begin"/>、 <paramref name="end"/>)
    /// </summary>
    /// <param name="hash">统一的散列代码。</param>
    /// <param name="begin">排他性下限约束</param>
    /// <param name="end">包含式上约束。</param>
    /// <returns><see langword="true"/> 当散列在范围之内时。</returns>
    /// <remarks>当 <paramref name="begin"/> 大于或等于 <paramref name="end"/> 时, 范围包
    /// 零周围和匹配的支盘高于 <paramref name="begin"/> 或位于或低于 <paramref name="end"/>。</remarks>
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
