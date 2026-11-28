using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 标识一项 <see cref="IReminderTable"/> 操作，该操作由 <see cref="IdealizedReminderTable"/> 观测到。
/// </summary>
public enum ReminderTableOperationKind
{
    /// <summary><see cref="IReminderTable.StartAsync"/>.</summary>
    Start,

    /// <summary><see cref="IReminderTable.StopAsync"/>.</summary>
    Stop,

    /// <summary><see cref="IReminderTable.ReadRow(GrainId, string)"/>.</summary>
    ReadRow,

    /// <summary><see cref="IReminderTable.ReadRows(GrainId)"/>.</summary>
    ReadGrainRows,

    /// <summary><see cref="IReminderTable.ReadRows(uint, uint)"/>.</summary>
    ReadRange,

    /// <summary><see cref="IReminderTable.UpsertRow(ReminderEntry)"/>.</summary>
    UpsertRow,

    /// <summary><see cref="IReminderTable.RemoveRow(GrainId, string, string)"/>.</summary>
    RemoveRow,

    /// <summary><see cref="IReminderTable.TestOnlyClearTable()"/>.</summary>
    ClearTable
}

/// <summary>
/// 当表已被标记为不可用以模拟存储中断时，由 <see cref="IdealizedReminderTable"/> 抛出。
/// </summary>
[Serializable]
[GenerateSerializer]
public sealed class ReminderTableUnavailableException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ReminderTableUnavailableException"/> 类的新实例。
    /// </summary>
    public ReminderTableUnavailableException() : base("The reminder table is unavailable.")
    {
    }

    /// <summary>
    /// 初始化 <see cref="ReminderTableUnavailableException"/> 类的新实例。
    /// </summary>
    /// <param name="message">消息。</param>
    public ReminderTableUnavailableException(string message) : base(message)
    {
    }

    [Obsolete]
    private ReminderTableUnavailableException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
/// 通过 <see cref="IdealizedReminderTable.Snapshot"/> 观测到的持久化提醒器记录。
/// </summary>
/// <param name="GrainId">Grain 标识符。</param>
/// <param name="ReminderName">提醒器名称。</param>
/// <param name="StartAt">持久化的开始时间。</param>
/// <param name="Period">持久化的周期。</param>
/// <param name="ETag">当前 ETag。</param>
/// <param name="PreviousETag">最近一次写入替换的 ETag（如果有）。</param>
/// <param name="Version">此记录单调递增的版本。</param>
public sealed record ReminderTableRecord(
    GrainId GrainId,
    string ReminderName,
    DateTime StartAt,
    TimeSpan Period,
    string ETag,
    string? PreviousETag,
    long Version)
{
    /// <summary>
    /// 获取决定范围所有权的均匀哈希码。
    /// </summary>
    public uint UniformHashCode => GrainId.GetUniformHashCode();

    /// <inheritdoc />
    public override string ToString()
        => $"(GrainId={GrainId}, ReminderName='{ReminderName}', StartAt={StartAt:O}, Period={Period}, ETag='{ETag}', PreviousETag={(PreviousETag is null ? "<null>" : $"'{PreviousETag}'")}, Version={Version}, Hash={UniformHashCode})";
}

/// <summary>
/// 由 <see cref="IdealizedReminderTable"/> 记录的一项操作。
/// </summary>
public sealed class ReminderTableOperation
{
    internal ReminderTableOperation(
        long sequence,
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
    {
        Sequence = sequence;
        Kind = kind;
        GrainId = grainId;
        ReminderName = reminderName;
        Begin = begin;
        End = end;
        SuppliedETag = suppliedETag;
        ResultETag = resultETag;
        Succeeded = succeeded;
        ResultCount = resultCount;
        Failure = failure;
    }

    /// <summary>获取单调递增的操作序列号。</summary>
    public long Sequence { get; }

    /// <summary>获取操作类型。</summary>
    public ReminderTableOperationKind Kind { get; }

    /// <summary>当操作针对某个 Grain 时，获取其标识。</summary>
    public GrainId? GrainId { get; }

    /// <summary>当操作针对某个提醒器时，获取其名称。</summary>
    public string? ReminderName { get; }

    /// <summary>对于范围读取，获取不含边界的下界。</summary>
    public uint? Begin { get; }

    /// <summary>对于范围读取，获取包含边界的上界。</summary>
    public uint? End { get; }

    /// <summary>对于条件操作，获取调用方提供的 ETag。</summary>
    public string? SuppliedETag { get; }

    /// <summary>获取写入操作返回给调用方的 ETag。</summary>
    public string? ResultETag { get; }

    /// <summary>获取一个值，指示操作是否在未抛出异常的情况下完成，并且对于
    /// <see cref="ReminderTableOperationKind.RemoveRow"/>，是否移除了一条记录。</summary>
    public bool Succeeded { get; }

    /// <summary>对于读取操作，获取返回的提醒器数量。</summary>
    public int ResultCount { get; }

    /// <summary>当操作抛出异常时，获取失败类型名称。</summary>
    public string? Failure { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var target = GrainId is { } grainId ? $" grain={grainId}" : string.Empty;
        var name = ReminderName is null ? string.Empty : $" reminder='{ReminderName}'";
        var range = Begin is { } begin && End is { } end ? $" range=({begin}, {end}]" : string.Empty;
        var supplied = SuppliedETag is null ? string.Empty : $" supplied='{SuppliedETag}'";
        var result = ResultETag is null ? string.Empty : $" etag='{ResultETag}'";
        var failure = Failure is null ? string.Empty : $" failure={Failure}";
        return $"#{Sequence} {Kind}{target}{name}{range}{supplied}{result} succeeded={Succeeded.ToString(CultureInfo.InvariantCulture)} count={ResultCount.ToString(CultureInfo.InvariantCulture)}{failure}";
    }
}

/// <summary>
/// 可等待的门控对象，阻塞下一次匹配的操作，直至门控被释放。
/// </summary>
public sealed class ReminderTableOperationGate : IAsyncDisposable
{
    private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IdealizedReminderTable _owner;
    private int _disposed;

    internal ReminderTableOperationGate(IdealizedReminderTable owner, ReminderTableOperationKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    /// <summary>
    /// 获取此门控阻塞的操作类型。
    /// </summary>
    public ReminderTableOperationKind Kind { get; }

    /// <summary>
    /// 等待匹配的操作到达门控。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作被阻塞时完成的任务。</returns>
    public Task WaitUntilBlockedAsync(CancellationToken cancellationToken = default) => _blocked.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// 释放被阻塞的操作。
    /// </summary>
    public void Release() => _release.TrySetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.RemoveGate(this);
            Release();
        }

        return ValueTask.CompletedTask;
    }

    internal void MarkBlocked() => _blocked.TrySetResult();

    internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => _release.Task.WaitAsync(cancellationToken);
}
