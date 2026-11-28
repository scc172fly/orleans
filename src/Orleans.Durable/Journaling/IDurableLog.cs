using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 表示一个仅追加、可观察的日志，其条目记录在持久化日志中。
/// </summary>
/// <typeparam name="T">日志条目的类型。</typeparam>
/// <remarks>
/// <para>
/// 每个追加条目获得持久化、单调递增序列号的下一个值。
/// 在同一历史中，序列号永不复用：<see cref="TrimBefore"/> 丢弃旧条目但保留
/// 序列，<see cref="Clear"/> 则在消耗一个序列号之后替换日志的条目。
/// 枚举按序列顺序返回保留条目。
/// </para>
/// <para>
/// <see cref="Epoch"/> 标识历史。删除持久化日志将结束该历史并重新开始序列，
/// 下一次变更以新的世代标识开始一段历史。因此，读取方位置由世代标识和序列号组成。
/// </para>
/// <para>
/// 草稿会在其持久化条目生成之前，将易失性内容以流的形式提供给包含草稿的读取方。草稿适用于
/// 模型响应等增量输出，其最终形式作为一个或多个条目提交。
/// </para>
/// <para>
/// <see cref="Seal"/> 持久化地结束日志，使读取方的流在最后一个条目之后结束。
/// </para>
/// <para>
/// 日志取得传入的条目与草稿项的所有权，并在追加每个条目时将其记入持久化日志。
/// 若要改变条目的内容，应追加新条目。
/// </para>
/// </remarks>
public interface IDurableLog<T> : IReadOnlyCollection<T>
{
    /// <summary>
    /// 获取日志历史的标识符；没有历史时为零。
    /// </summary>
    /// <remarks>
    /// 日志创建或其持久化日志删除后的首次变更，将以新的随机
    /// 正数世代标识开始一段历史。该标识随历史记入日志，因此在每个激活实例中均标识这段历史。
    /// </remarks>
    long Epoch { get; }

    /// <summary>
    /// 获取首个保留条目的序列号。
    /// </summary>
    long FirstSequence { get; }

    /// <summary>
    /// 获取下一条追加条目将获得的序列号。
    /// </summary>
    long NextSequence { get; }

    /// <summary>
    /// 获取最后一个已提交条目之后的序列号。序列号更小的条目已持久化。
    /// </summary>
    long CommittedSequence { get; }

    /// <summary>
    /// 获取一个值，指示 <see cref="Seal"/> 是否已结束日志。
    /// </summary>
    bool IsSealed { get; }

    /// <summary>
    /// 获取具有指定序列号的保留条目。
    /// </summary>
    /// <param name="sequence">条目的序列号。</param>
    /// <returns>条目。</returns>
    /// <exception cref="ArgumentOutOfRangeException">日志未保留序列号为 <paramref name="sequence"/> 的条目。</exception>
    T this[long sequence] { get; }

    /// <summary>
    /// 尝试获取具有指定序列号的保留条目。
    /// </summary>
    /// <param name="sequence">条目的序列号。</param>
    /// <param name="item">条目（如果仍被保留）。</param>
    /// <returns><see langword="true"/> 表示日志保留序列号为 <paramref name="sequence"/> 的条目；否则为 <see langword="false"/>。</returns>
    bool TryGet(long sequence, [MaybeNullWhen(false)] out T item);

    /// <summary>
    /// 向日志追加条目。
    /// </summary>
    /// <param name="item">条目。</param>
    /// <returns>条目的序列号。</returns>
    /// <exception cref="InvalidOperationException">日志已封存。</exception>
    long Append(T item);

    /// <summary>
    /// 丢弃序列号小于 <paramref name="sequence"/> 的条目。
    /// </summary>
    /// <param name="sequence">首个要保留条目的序列号。</param>
    /// <remarks>
    /// 裁剪限制保留范围并保留 <see cref="NextSequence"/>；仍保留的条目继续有效。裁剪持久化后，每个活跃的
    /// 读取方都会收到 <see cref="LogEventKind.Trimmed"/>，其中包含新的 <see cref="FirstSequence"/>，
    /// 位于该位置之前的读取方从该位置继续读取。已封存日志也可以裁剪。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence"/> 为负数或大于 <see cref="NextSequence"/>。</exception>
    void TrimBefore(long sequence);

    /// <summary>
    /// 丢弃所有条目，例如为了使用压实后的摘要替换被丢弃的条目。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 清空消耗一个序列号并保留 <see cref="Epoch"/>；之后，<see cref="FirstSequence"/> 与
    /// <see cref="NextSequence"/> 等于原来的 <see cref="NextSequence"/> 加一。位于被清空序列号处或之前的
    /// 读取方已经观察到被清空操作替换的条目。它会收到 <see cref="LogEventKind.Reset"/>，
    /// 即使在后续激活实例中恢复读取也会收到；位于该位置之后的读取方则不会收到。读取方在
    /// 清空持久化后收到此事件。
    /// </para>
    /// <para>
    /// 若要丢弃后继条目仍然有效的旧条目，请改用 <see cref="TrimBefore"/>。打开的草稿保持打开。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">日志已封存。</exception>
    void Clear();

    /// <summary>
    /// 持久化地结束日志。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 封存操作持久化后，每个读取方收到剩余条目，随后收到 <see cref="LogEventKind.Sealed"/>，
    /// 其流随即结束。开始读取已封存日志的读取方会收到保留条目，然后收到
    /// <see cref="LogEventKind.Sealed"/>。因激活实例停止而结束的流不会收到该事件。
    /// </para>
    /// <para>
    /// 封存通过 <see cref="LogDraftAbandonReason.Sealed"/> 放弃打开的草稿。已封存日志拒绝 <see cref="Append"/>、
    /// <see cref="Clear"/> 和 <see cref="BeginDraft"/>，但仍可裁剪。再次封存已封存日志不会产生效果。
    /// 只有删除持久化日志才能解除封存。
    /// </para>
    /// </remarks>
    void Seal();

    /// <summary>
    /// 打开一个草稿，向包含草稿的读取方流式传输易失性项。
    /// </summary>
    /// <returns>已打开的草稿。若草稿尚未关闭，则释放它会放弃草稿。</returns>
    /// <exception cref="InvalidOperationException">日志已封存。</exception>
    IDurableLogDraft<T> BeginDraft();

    /// <summary>
    /// 从某个位置读取日志已提交的条目，随后等待新条目。
    /// </summary>
    /// <param name="fromSequence">首个要读取条目的序列号。</param>
    /// <param name="epoch">
    /// <paramref name="fromSequence"/> 所属历史的世代标识，来自 <see cref="LogEvent{T}.Epoch"/>；或使用零
    /// 读取当前历史。当世代标识与 <see cref="Epoch"/> 不同时，流以下列事件开始：
    /// <see cref="LogEventKind.Reset"/>.
    /// </param>
    /// <param name="includeDrafts">
    /// 流是否还投递草稿事件：<see cref="LogEventKind.DraftItem"/>、<see cref="LogEventKind.DraftCommitted"/>
    /// 和 <see cref="LogEventKind.DraftAbandoned"/>。
    /// </param>
    /// <param name="resumeDraftId">
    /// 包含草稿的读取方重新连接时此前正在查看的草稿。如果草稿仍打开，
    /// 流从头重复其各项。如果草稿已提交，流报告
    /// <see cref="LogEventKind.DraftCommitted"/> 以及替换该草稿的条目；只要日志仍保留这些条目中的首条，无论在哪个激活实例中均如此。
    /// 如果草稿在此激活实例中关闭而未提交，流报告
    /// <see cref="LogEventKind.DraftAbandoned"/>，其中包含其 <see cref="LogDraftAbandonReason"/>。否则，流对其他激活实例打开的草稿报告
    /// <see cref="LogDraftAbandonReason.PreviousActivation"/>，对此激活实例的草稿报告
    /// <see cref="LogDraftAbandonReason.Unknown"/>。
    /// </param>
    /// <param name="cancellationToken">用于结束读取的令牌。</param>
    /// <returns>
    /// 按序列顺序提供已提交条目的流。包含条目的日志写入获得
    /// 确认后即投递该条目。当 <paramref name="includeDrafts"/> 为 <see langword="true"/> 时，流还会在草稿项写入时
    /// 投递它们，并在替换草稿的条目持久化后报告已提交草稿。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 读取方从自身位置重新读取保留条目，而不是缓冲这些条目，因此慢速读取方不会额外保留条目。
    /// 流以 <see cref="LogEventKind.Reset"/> 开始的情况包括：<paramref name="epoch"/> 指定了另一段历史，
    /// <paramref name="fromSequence"/> 位于被清空的序列号处或之前，或者位于 <see cref="NextSequence"/> 之后；
    /// 若位置在首个保留序列号之前，则以 <see cref="LogEventKind.Trimmed"/> 开始，首个保留序列号由 <see cref="FirstSequence"/> 指定。两种情况下均从
    /// 首个保留条目继续。投递完读取开始时所有已提交的条目后，流会
    /// 投递一次 <see cref="LogEventKind.CaughtUp"/>。
    /// </para>
    /// <para>
    /// 若要在断开连接后恢复读取，传入最新事件的世代标识及读取方最后收到的条目
    /// 之后的序列号。在所属激活实例的执行上下文中枚举该流。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fromSequence"/> 或 <paramref name="epoch"/> 为负数。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 枚举在所属管理器恢复状态之前开始，或在日志操作失败导致管理器停止之后开始。
    /// </exception>
    IAsyncEnumerable<LogEvent<T>> ReadAsync(
        long fromSequence = 0,
        long epoch = 0,
        bool includeDrafts = false,
        LogDraftId? resumeDraftId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 向包含草稿的 <see cref="IDurableLog{T}"/> 读取方流式传输易失性项，直至草稿提交
/// 持久化条目或被放弃。
/// </summary>
/// <typeparam name="T">项和条目的类型。</typeparam>
/// <remarks>
/// <para>
/// 草稿仅在打开期间将其各项保存在内存中。关闭草稿会释放这些项，因此如果读取方在草稿关闭时尚未
/// 读完每个项，则接下来收到草稿结果，不再收到剩余各项。
/// 已提交条目承载草稿的最终内容。
/// </para>
/// <para>
/// 草稿在其所有者提交、放弃或释放它时关闭；当日志因其持久化日志
/// 被删除或日志被封存而关闭草稿时也会如此。释放打开的草稿会放弃它。在所属激活实例的执行上下文中使用草稿；
/// 提交可能已被日志关闭的草稿之前，请检查 <see cref="IsOpen"/>。
/// </para>
/// </remarks>
public interface IDurableLogDraft<T> : IDisposable
{
    /// <summary>
    /// 获取草稿的标识符。
    /// </summary>
    LogDraftId Id { get; }

    /// <summary>
    /// 获取一个值，指示草稿是否打开。
    /// </summary>
    /// <remarks>
    /// 当草稿被提交、放弃、释放或由日志关闭时，该值变为 <see langword="false"/>。
    /// <see cref="Write"/> 和 <see cref="Commit"/> 对已关闭草稿抛出 <see cref="InvalidOperationException"/>。
    /// </remarks>
    bool IsOpen { get; }

    /// <summary>
    /// 获取写入打开草稿的项数。
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 写入一个易失性项，并将其投递给包含草稿的读取方。
    /// </summary>
    /// <param name="item">该项。</param>
    /// <exception cref="InvalidOperationException">草稿已关闭。异常消息说明原因。</exception>
    void Write(T item);

    /// <summary>
    /// 关闭草稿，并追加用于替换其各项的持久化条目。
    /// </summary>
    /// <param name="entries">按顺序排列的条目。传入零个条目可正常关闭草稿，而不追加条目。</param>
    /// <returns>
    /// 首个条目的序列号。当 <paramref name="entries"/> 为空时，返回下一条
    /// 追加条目将获得的序列号。
    /// </returns>
    /// <remarks>
    /// 所有条目作为单条日志条目追加，因此一次日志写入使它们全部持久化。读取方收到
    /// <see cref="LogEventKind.DraftCommitted"/>（包含已提交范围），随后收到条目。如果某个条目无法编码，
    /// 异常会传播，不追加任何条目，且草稿保持打开。
    /// </remarks>
    /// <exception cref="InvalidOperationException">草稿已关闭。异常消息说明原因。</exception>
    long Commit(params ReadOnlySpan<T> entries);

    /// <summary>
    /// 关闭草稿而不追加条目，并报告 <see cref="LogEventKind.DraftAbandoned"/>，同时附带
    /// <see cref="LogDraftAbandonReason.Abandoned"/>.
    /// </summary>
    /// <param name="message">在 <see cref="LogEvent{T}.Message"/> 中报告给读取方的失败说明。</param>
    /// <remarks>放弃已关闭草稿没有效果。使用 <see cref="Commit"/> 且不传入条目可在没有失败的情况下关闭草稿。</remarks>
    void Abandon(string? message = null);
}

/// <summary>
/// 标识 <see cref="IDurableLog{T}"/> 的一个草稿。
/// </summary>
/// <remarks>
/// <see cref="InstanceId"/> 标识打开草稿的日志实例，因此也标识激活实例。读取方
/// 重新连接到新激活实例时可据此得知草稿的易失性内容已经丢失，除非该草稿已提交。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Journaling.LogDraftId")]
public readonly record struct LogDraftId
{
    /// <summary>
    /// 初始化 <see cref="LogDraftId"/> 结构的新实例。
    /// </summary>
    /// <param name="instanceId">打开草稿的日志实例标识符。</param>
    /// <param name="number">草稿在该实例内的编号。它不是负数。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> 为负数。</exception>
    public LogDraftId(Guid instanceId, long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        InstanceId = instanceId;
        Number = number;
    }

    /// <summary>
    /// 获取打开草稿的日志实例标识符，从而也标识其激活实例。
    /// </summary>
    [Id(0)]
    public Guid InstanceId { get; }

    /// <summary>
    /// 获取草稿在其日志实例内的编号。
    /// </summary>
    [Id(1)]
    public long Number { get; }

    /// <summary>
    /// 将标识符格式化为 <c>{InstanceId:N}.{Number}</c>。
    /// </summary>
    /// <returns>格式化后的标识符。</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{InstanceId:N}.{Number}");

    /// <summary>
    /// 解析通过 <see cref="ToString"/> 格式化的标识符。
    /// </summary>
    /// <param name="value">格式化后的标识符。</param>
    /// <param name="result">解析出的标识符。</param>
    /// <returns><see langword="true"/> 表示 <paramref name="value"/> 是有效标识符；否则为 <see langword="false"/>。</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out LogDraftId result)
    {
        var separator = value.IndexOf('.');
        if (separator > 0
            && Guid.TryParseExact(value[..separator], "N", out var instanceId)
            && long.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            result = new(instanceId, number);
            return true;
        }

        result = default;
        return false;
    }
}

/// <summary>
/// 标识 <see cref="LogEvent{T}"/> 的事件种类。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.LogEventKind")]
public enum LogEventKind : byte
{
    /// <summary>
    /// <see cref="LogEvent{T}.Item"/> 是序列号为 <see cref="LogEvent{T}.Sequence"/> 的条目。
    /// </summary>
    Entry = 0,

    /// <summary>
    /// 读取方观察到的条目已被替换，因为 <see cref="IDurableLog{T}.Clear"/> 丢弃了这些条目、
    /// 持久化日志被删除、读取方从另一世代的历史恢复，或读取方的位置在日志末尾
    /// 之后。应丢弃在该事件之前读到的所有条目。流从 <see cref="LogEvent{T}.Sequence"/> 继续，该值为
    /// <see cref="LogEvent{T}.Epoch"/> 所标识历史的首个保留条目。
    /// </summary>
    Reset = 1,

    /// <summary>
    /// <see cref="LogEvent{T}.Item"/> 是草稿中位置为 <see cref="LogEvent{T}.Sequence"/> 的易失性项，草稿标识为
    /// <see cref="LogEvent{T}.DraftId"/>。位置零表示草稿内容的起始位置。
    /// </summary>
    DraftItem = 2,

    /// <summary>
    /// 草稿 <see cref="LogEvent{T}.DraftId"/> 已关闭，其内容被以下数量的条目替换：<see cref="LogEvent{T}.Count"/>
    /// ，这些条目从序列号 <see cref="LogEvent{T}.Sequence"/> 开始。应丢弃其各项。数量为零表示
    /// 草稿在不追加条目的情况下正常关闭。
    /// </summary>
    DraftCommitted = 3,

    /// <summary>
    /// 草稿 <see cref="LogEvent{T}.DraftId"/> 因 <see cref="LogEvent{T}.AbandonReason"/> 中的原因而关闭，未追加条目。
    /// 应丢弃其各项。
    /// </summary>
    DraftAbandoned = 4,

    /// <summary>
    /// <see cref="IDurableLog{T}.TrimBefore"/> 丢弃了 <see cref="LogEvent{T}.Sequence"/> 之前的条目，后者是新的
    /// 首个保留条目。从该序列号起读到的条目仍有效。位于该位置之前的读取方从此位置继续。
    /// </summary>
    Trimmed = 5,

    /// <summary>
    /// 流已投递读取开始时所有已提交条目。<see cref="LogEvent{T}.Sequence"/> 为
    /// 读取方位置。流仅投递此事件一次。
    /// </summary>
    CaughtUp = 6,

    /// <summary>
    /// <see cref="IDurableLog{T}.Seal"/> 结束日志，日志的条目在 <see cref="LogEvent{T}.Sequence"/> 之前结束。这是
    /// 流的最后一个事件。
    /// </summary>
    Sealed = 7,
}

/// <summary>
/// 标识 <see cref="IDurableLog{T}"/> 的草稿关闭且未追加条目的原因。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.LogDraftAbandonReason")]
public enum LogDraftAbandonReason : byte
{
    /// <summary>
    /// 该事件不描述已放弃的草稿。
    /// </summary>
    None = 0,

    /// <summary>
    /// 所有者通过 <see cref="IDurableLogDraft{T}.Abandon"/> 放弃草稿。
    /// </summary>
    Abandoned = 1,

    /// <summary>
    /// 所有者释放草稿而未将其关闭。
    /// </summary>
    Disposed = 2,

    /// <summary>
    /// 日志的持久化日志已删除。
    /// </summary>
    JournalDeleted = 3,

    /// <summary>
    /// 其他激活实例打开了该草稿，日志未保留其提交记录。草稿仅存在于内存中，因此其
    /// 内容已丢失。
    /// </summary>
    PreviousActivation = 4,

    /// <summary>
    /// 此激活实例打开了该草稿（或草稿声称由它打开），但未保留提交记录，也未记录其关闭原因。
    /// </summary>
    Unknown = 5,

    /// <summary>
    /// 草稿打开期间，<see cref="IDurableLog{T}.Seal"/> 结束了日志。
    /// </summary>
    Sealed = 6,
}

/// <summary>
/// 描述从 <see cref="IDurableLog{T}"/> 读取的条目或事件。
/// </summary>
/// <typeparam name="T">日志条目的类型。</typeparam>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.LogEvent`1")]
public readonly record struct LogEvent<T>
{
    /// <summary>
    /// 获取事件种类。
    /// </summary>
    [Id(0)]
    public LogEventKind Kind { get; init; }

    /// <summary>
    /// 获取条目序列号；对于 <see cref="LogEventKind.Reset"/> 和
    /// <see cref="LogEventKind.Trimmed"/>，获取首个保留序列号；对于 <see cref="LogEventKind.CaughtUp"/> 和 <see cref="LogEventKind.Sealed"/>，获取位置；或者获取草稿项在其草稿中的位置，或
    /// 已提交草稿条目的首个序列号。
    /// </summary>
    [Id(1)]
    public long Sequence { get; init; }

    /// <summary>
    /// 对于 <see cref="LogEventKind.Entry"/> 获取条目，对于 <see cref="LogEventKind.DraftItem"/> 获取草稿项。
    /// </summary>
    [Id(2)]
    public T? Item { get; init; }

    /// <summary>
    /// 获取草稿事件的草稿标识符。
    /// </summary>
    [Id(3)]
    public LogDraftId? DraftId { get; init; }

    /// <summary>
    /// 获取所有者传给 <see cref="IDurableLogDraft{T}.Abandon"/> 的说明，用于 <see cref="LogEventKind.DraftAbandoned"/> 事件。
    /// </summary>
    [Id(4)]
    public string? Message { get; init; }

    /// <summary>
    /// 对于 <see cref="LogEventKind.DraftCommitted"/>，获取替换草稿的条目数量。
    /// </summary>
    [Id(5)]
    public int Count { get; init; }

    /// <summary>
    /// 对于 <see cref="LogEventKind.DraftAbandoned"/>，获取草稿关闭且未追加条目的原因。
    /// </summary>
    [Id(6)]
    public LogDraftAbandonReason AbandonReason { get; init; }

    /// <summary>
    /// 获取事件所属历史的世代标识。将其与序列号一起传入以恢复读取。
    /// </summary>
    [Id(7)]
    public long Epoch { get; init; }
}

/// <summary>
/// 记录 <see cref="IDurableLog{T}"/> 的草稿已提交从 <see cref="Sequence"/> 开始的条目。
/// </summary>
/// <param name="DraftId">草稿的标识符。</param>
/// <param name="Sequence">首个已提交条目的序列号。</param>
/// <param name="Count">已提交条目的数量。</param>
public readonly record struct LogDraftCommit(LogDraftId DraftId, long Sequence, int Count);
