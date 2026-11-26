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
/// 指定 <see cref="IReminderTable"/> 观察的 <see cref="IdealizedReminderTable"/>.
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
/// 当表格标记无法模拟存储中断时被 <see cref="IdealizedReminderTable"/> 扔出 。
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
    /// <param name="message">电文。</param>
    public ReminderTableUnavailableException(string message) : base(message)
    {
    }

    [Obsolete]
    private ReminderTableUnavailableException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
/// 通过 <see cref="IdealizedReminderTable.Snapshot"/>观察到的耐久提醒记录。
/// </summary>
/// <param name="GrainId"> grain 标识符。</param>
/// <param name="ReminderName">提醒的名称。</param>
/// <param name="StartAt">持续启动时间。</param>
/// <param name="Period">持续时间。</param>
/// <param name="ETag">当前的ETag。</param>
/// <param name="PreviousETag">以最新文章取代了ETag。</param>
/// <param name="Version">本记录单声道增加的版本。</param>
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
    /// 获得统一的散列代码,确定范围所有权。
    /// </summary>
    public uint UniformHashCode => GrainId.GetUniformHashCode();

    /// <inheritdoc />
    public override string ToString()
        => $"(GrainId={GrainId}, ReminderName='{ReminderName}', StartAt={StartAt:O}, Period={Period}, ETag='{ETag}', PreviousETag={(PreviousETag is null ? "<null>" : $"'{PreviousETag}'")}, Version={Version}, Hash={UniformHashCode})";
}

/// <summary>
/// 记录到 <see cref="IdealizedReminderTable"/>的一个操作。
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

    /// <summary>获得单声道增加的操作序列号。</summary>
    public long Sequence { get; }

    /// <summary>获得操作类型。</summary>
    public ReminderTableOperationKind Kind { get; }

    /// <summary>当操作目标为一时, 获得 grain 识别符号。</summary>
    public GrainId? GrainId { get; }

    /// <summary>当操作目标为一时, 获得提醒名称。</summary>
    public string? ReminderName { get; }

    /// <summary>获得排他性下限, 范围读作。</summary>
    public uint? Begin { get; }

    /// <summary>获得包含性的上约束,范围为。</summary>
    public uint? End { get; }

    /// <summary>获得电话提供ETAG,有条件操作。</summary>
    public string? SuppliedETag { get; }

    /// <summary>找回来电者,写作</summary>
    public string? ResultETag { get; }

    /// <summary>获得一个数值,表明是否未投掷就完成了操作,并且
    /// <see cref="ReminderTableOperationKind.RemoveRow"/>,删除一行。</summary>
    public bool Succeeded { get; }

    /// <summary>获取回送提醒函的数, 读作 。</summary>
    public int ResultCount { get; }

    /// <summary>选择在任务丢弃时失败类型名称。</summary>
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
/// 等待的大门 阻断下一个匹配操作 直到它被释放
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
    /// 得到的操作类型 这样的门挡。
    /// </summary>
    public ReminderTableOperationKind Kind { get; }

    /// <summary>
    /// 等待匹配操作到达大门
    /// </summary>
    /// <param name="cancellationToken">取消的符号。</param>
    /// <returns>任务在操作被阻断时完成。</returns>
    public Task WaitUntilBlockedAsync(CancellationToken cancellationToken = default) => _blocked.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// 释放被封存的操作。
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
