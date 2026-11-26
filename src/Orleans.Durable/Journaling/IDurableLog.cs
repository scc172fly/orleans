using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 仅代表一个仅附加的可观测日志,其条目记录在日记中。
/// </summary>
/// <typeparam name="T">日志中条目的类型。</typeparam>
/// <remarks>
/// <para>
/// 每个附加条目的下一个值为耐久、单质增长序列号。
/// 序列号在历史中从未再使用: <see cref="TrimBefore"/> 丢弃旧条目,同时保存
/// 序列,并且 <see cref="Clear"/> 在消耗一个序列编号后替换日志的条目。
/// 数字按顺序排列保留条目。
/// </para>
/// <para>
/// <see cref="Epoch"/> 识别历史。删除日记结束历史并重新启动序列,然后
/// 下一个变化将以新时代开始历史。 因此, 阅读器位置是一个时代和一个序列编号 。
/// </para>
/// <para>
/// 向读者流出挥发性内容的草稿,其中包括在耐用条目存在之前的草稿。
/// 增量产出,例如示范答复,其最终形式作为一个或多个条目承诺。
/// </para>
/// <para>
/// <see cref="Seal"/> 长期结束日志, 这样读者的流在最后输入后结束 。
/// </para>
/// <para>
/// 日志取得条目的所有权和传递给它的项目的草稿,日记记录附在后面的每个条目。
/// 要更改条目的内容,请附加新的条目。
/// </para>
/// </remarks>
public interface IDurableLog<T> : IReadOnlyCollection<T>
{
    /// <summary>
    /// 获取日志历史的识别符号,或者当日志没有历史时为零。
    /// </summary>
    /// <remarks>
    /// 日志创建后, 或日记日记被删除后, 以新的随机开始历史
    /// 阳性时代。 这个时代与历史同步, 所以它在每个激活中都识别历史 。
    /// </remarks>
    long Epoch { get; }

    /// <summary>
    /// 获取第一个保留条目的序列编号。
    /// </summary>
    long FirstSequence { get; }

    /// <summary>
    /// 获取下一个附加条目收到的序列号。
    /// </summary>
    long NextSequence { get; }

    /// <summary>
    /// 获取上次承诺条目之后的序列编号。序列号较低的条目是耐久的。
    /// </summary>
    long CommittedSequence { get; }

    /// <summary>
    /// 获得一个值,以显示 <see cref="Seal"/> 是否结束了日志。
    /// </summary>
    bool IsSealed { get; }

    /// <summary>
    /// 获取保留条目的指定序列号。
    /// </summary>
    /// <param name="sequence">条目序列号。</param>
    /// <returns>条目。</returns>
    /// <exception cref="ArgumentOutOfRangeException">日志不保留 <paramref name="sequence"/>的条目。</exception>
    T this[long sequence] { get; }

    /// <summary>
    /// 尝试获取保留条目,并使用指定的序列号。
    /// </summary>
    /// <param name="sequence">条目序列号。</param>
    /// <param name="item">如果保留,则保留条目。</param>
    /// <returns><see langword="true"/> ,如果日志保留了 <paramref name="sequence"/>条目;否则, <see langword="false"/>。</returns>
    bool TryGet(long sequence, [MaybeNullWhen(false)] out T item);

    /// <summary>
    /// 将条目添加到日志中。
    /// </summary>
    /// <param name="item">条目。</param>
    /// <returns>条目序列号。</returns>
    /// <exception cref="InvalidOperationException">日志已密封。</exception>
    long Append(T item);

    /// <summary>
    /// 丢弃序列号低于 <paramref name="sequence"/>的条目。
    /// </summary>
    /// <param name="sequence">要保留的第一个条目的序列号 。</param>
    /// <remarks>
    /// triming 边框保留和保存 <see cref="NextSequence"/>;留存条目仍然有效。
    /// 接收读者 <see cref="LogEventKind.Trimmed"/> 与新 <see cref="FirstSequence"/> 一旦修饰持久,
    /// 并有一个读者,在它继续之前,可以修补密封的日志。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence"/> 为负值或大于 <see cref="NextSequence"/>。</exception>
    void TrimBefore(long sequence);

    /// <summary>
    /// 丢弃每个条目, 例如用压缩摘要替换废弃的条目 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 清除工作消耗一个序列号并保存 <see cref="Epoch"/>:随后, <see cref="FirstSequence"/> 和
    /// <see cref="NextSequence"/> 等于上一个 <see cref="NextSequence"/> 加1。
    /// 清除序列号已观察到被替换的清晰条目。它收到 <see cref="LogEventKind.Reset"/>,
    /// 包括当它恢复后再激活时, 而一个读者在它不恢复后的位置就定位。 读者会接收事件
    /// 明理是持久的。
    /// </para>
    /// <para>
    /// 使用 <see cref="TrimBefore"/> 代替丢弃继承人仍然有效的旧条目。 开放式草稿仍然开放 。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">日志已密封。</exception>
    void Clear();

    /// <summary>
    /// 极有可能结束日志
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一旦封条具有耐久性,每个读者将接收其余条目,然后 <see cref="LogEventKind.Sealed"/>,
    /// 开始读取密封日志的读者会接收其保留条目,然后
    /// <see cref="LogEventKind.Sealed"/>. 一条流,该流因启动停止而终止,没有事件结束。
    /// </para>
    /// <para>
    /// 密封丢弃 <see cref="LogDraftAbandonReason.Sealed"/>打开的草稿。密封日志拒绝 <see cref="Append"/>,
    /// <see cref="Clear"/>和 <see cref="BeginDraft"/>,仍然可以剪裁。密封的日志封存没有效果。
    /// 只删除日记, 解封日志 。
    /// </para>
    /// </remarks>
    void Seal();

    /// <summary>
    /// 打开将挥发性项目流到读者的草稿,其中包括草案。
    /// </summary>
    /// <returns>公开草案,放弃该草案,除非它关闭。</returns>
    /// <exception cref="InvalidOperationException">日志已密封。</exception>
    IDurableLogDraft<T> BeginDraft();

    /// <summary>
    /// 从位置读取日志的承付条目,然后等待新条目。
    /// </summary>
    /// <param name="fromSequence">第一个条目的序列编号为 。</param>
    /// <param name="epoch">
    /// <paramref name="fromSequence"/> 属于历史的时代, 从 <see cref="LogEvent{T}.Epoch"/>或零
    /// 以读取当前历史。当时代与 <see cref="Epoch"/>不同时,流开始于
    /// <see cref="LogEventKind.Reset"/>.
    /// </param>
    /// <param name="includeDrafts">
    /// 流体是否也提供事件草案: <see cref="LogEventKind.DraftItem"/>、 <see cref="LogEventKind.DraftCommitted"/>、
    /// 和 <see cref="LogEventKind.DraftAbandoned"/>。
    /// </param>
    /// <param name="resumeDraftId">
    /// 草案是重新连接的读者,其中包括正在查看的草稿。
    /// 数据流从开始重复其项目。当它承诺时,报告数据流
    /// <see cref="LogEventKind.DraftCommitted"/> ,加上替换条目的条目,在任何激活中,在任何激活时,记录保留
    /// 。 当它关闭此激活时不承诺关闭时, 流报告
    /// <see cref="LogEventKind.DraftAbandoned"/> 其与本 <see cref="LogDraftAbandonReason"/>否则,各流报告
    /// <see cref="LogDraftAbandonReason.PreviousActivation"/> ,用于在另一启动下打开的草案
    /// 用于此激活的草稿<see cref="LogDraftAbandonReason.Unknown"/> 。
    /// </param>
    /// <param name="cancellationToken">结束读的符号。</param>
    /// <returns>
    /// 以序列顺序排列的一串承诺条目。每条条目在包含该条目的日记写后即交付
    /// 当 <paramref name="includeDrafts"/> 是 <see langword="true"/>时,流中还提供项目草案
    /// 它们是书面的,一旦取代这些文字的条目具有持久性,则报告一份承诺的草案。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 读者重新阅读从位置上保留的条目,而不是缓冲它们,因此一个缓慢的读者不保留条目。
    /// 流流以 <see cref="LogEventKind.Reset"/> 开始,当 <paramref name="epoch"/> 指定另一个历史历史时,当
    /// <paramref name="fromSequence"/> 位于或高于清除的序列号,或与 <see cref="NextSequence"/>相继时,
    /// 和 <see cref="LogEventKind.Trimmed"/> 在那日, <see cref="FirstSequence"/>. Either way, it continues from
    /// 第一个保留条目。 它发送了当读开始时所执行的每一条目后, 流会传送
    /// 一次<see cref="LogEventKind.CaughtUp"/> 。
    /// </para>
    /// <para>
    /// 断开后要恢复, 传递最新事件的时代和最后条目之后的序列, 读者
    /// 。在自控激活执行背景下,接收到的流数。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fromSequence"/> 或 <paramref name="epoch"/> 为负值。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 数字在拥有权经理恢复状态之前开始计算,或在日记操作失败后停止计算。
    /// </exception>
    IAsyncEnumerable<LogEvent<T>> ReadAsync(
        long fromSequence = 0,
        long epoch = 0,
        bool includeDrafts = false,
        LogDraftId? resumeDraftId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 向读者提供流流波动性物品 <see cref="IDurableLog{T}"/> 包括草案在内的草案,直至草案作出承诺
/// 耐久条目或废弃条目。
/// </summary>
/// <typeparam name="T">项目和条目的类型。</typeparam>
/// <remarks>
/// <para>
/// 草案开放时才保留其项目。
/// 草案结束后的每一项目在下一个收到草案结果时都读,而从未收到其余的项目。
/// 承诺条目包含草案的最终内容。
/// </para>
/// <para>
/// 草稿在所有人实施、遗弃或处置时,以及当日志因日志日记而关闭时,草稿将关闭
/// 保存未打开的草稿时放弃它。 在自定义激活执行背景中使用草稿并检查
/// 在承诺日志可能已经关闭的草稿之前,先<see cref="IsOpen"/> 。
/// </para>
/// </remarks>
public interface IDurableLogDraft<T> : IDisposable
{
    /// <summary>
    /// 拿到了草稿的识别资料
    /// </summary>
    LogDraftId Id { get; }

    /// <summary>
    /// 获得显示草稿是否打开的值 。
    /// </summary>
    /// <remarks>
    /// 当草案被承诺、废弃或处置时,当日志关闭时,该数值即为 <see langword="false"/> 。
    /// <see cref="Write"/> 和 <see cref="Commit"/> 投出 <see cref="InvalidOperationException"/> ,以备完成草稿。
    /// </remarks>
    bool IsOpen { get; }

    /// <summary>
    /// 获取打开草稿中写入的项目数。
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 写入一个波动的项目, 并传送给读者, 包括草稿。
    /// </summary>
    /// <param name="item">这个项目</param>
    /// <exception cref="InvalidOperationException">草稿已经关闭,信息说明了原因</exception>
    void Write(T item);

    /// <summary>
    /// 关闭草稿并附加替换其项目的耐久条目。
    /// </summary>
    /// <param name="entries">条目按顺序排列。通过 否 , 无法在不附加条目的情况下干净地关闭草稿 。</param>
    /// <returns>
    /// 第一个条目的序列编号。当 <paramref name="entries"/> 是空时,下一个序列号
    /// 收到所附条目。
    /// </returns>
    /// <remarks>
    /// 条目作为一份日记条目附后,因此一份日记的写作可以使所有条目都持久。读者会收到这些条目。
    /// 与承诺范围<see cref="LogEventKind.DraftCommitted"/> 相伴的条目,然后是条目。如果一个条目无法编码,
    /// 例外宣传,不附加条目,草案仍然开放。
    /// </remarks>
    /// <exception cref="InvalidOperationException">草稿已经关闭,信息说明了原因</exception>
    long Commit(params ReadOnlySpan<T> entries);

    /// <summary>
    /// 关闭没有条目的草稿和报告 <see cref="LogEventKind.DraftAbandoned"/>
    /// <see cref="LogDraftAbandonReason.Abandoned"/>.
    /// </summary>
    /// <param name="message">描述未在英文、 <see cref="LogEvent{T}.Message"/>.</param>
    /// <remarks>放弃已完成的草案没有效果。使用无条目的 <see cref="Commit"/> 来不失败地关闭草稿。</remarks>
    void Abandon(string? message = null);
}

/// <summary>
/// 确定草案的草案 <see cref="IDurableLog{T}"/>.
/// </summary>
/// <remarks>
/// <see cref="InstanceId"/> 识别日志实例,从而确定打开草稿的激活。
/// 因此,与新激活重新连接后可以了解到,除非草案承诺实施,否则草案的波动性内容已经丧失。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Journaling.LogDraftId")]
public readonly record struct LogDraftId
{
    /// <summary>
    /// 初始化 <see cref="LogDraftId"/> 结构的新实例。
    /// </summary>
    /// <param name="instanceId">打开草稿的日志实例标识符。</param>
    /// <param name="number">草案的编号在此实例中。 它不是负数。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> 为负。</exception>
    public LogDraftId(Guid instanceId, long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        InstanceId = instanceId;
        Number = number;
    }

    /// <summary>
    /// 获取日志实例的标识符, 从而获得打开草稿的激活 。
    /// </summary>
    [Id(0)]
    public Guid InstanceId { get; }

    /// <summary>
    /// 将草稿的编号放到日志实例中。
    /// </summary>
    [Id(1)]
    public long Number { get; }

    /// <summary>
    /// 将识别符号格式为 <c>{InstanceId:N}.{Number}</c>。
    /// </summary>
    /// <returns>格式化标识符。</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{InstanceId:N}.{Number}");

    /// <summary>
    /// 分析 <see cref="ToString"/>格式的标识符。
    /// </summary>
    /// <param name="value">格式化标识符。</param>
    /// <param name="result">解析标识符。</param>
    /// <returns><see langword="true"/> 如果 <paramref name="value"/> 是一个有效的标识符; 否则, <see langword="false"/>.</returns>
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
/// 标出一种 <see cref="LogEvent{T}"/>。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.LogEventKind")]
public enum LogEventKind : byte
{
    /// <summary>
    /// <see cref="LogEvent{T}.Item"/> 是序列号为 <see cref="LogEvent{T}.Sequence"/>的条目。
    /// </summary>
    Entry = 0,

    /// <summary>
    /// 读者所观察到的条目被替换,因为 <see cref="IDurableLog{T}.Clear"/> 丢弃了这些条目,
    /// 日记被删除, 读者从另一个时代的历史中恢复到其它时代, 或者读者的立场在
    /// 日志。丢弃此事件之前读取的任何条目。流从 <see cref="LogEvent{T}.Sequence"/>、
    /// <see cref="LogEvent{T}.Epoch"/>所标明的历史历史的首个保留条目。
    /// </summary>
    Reset = 1,

    /// <summary>
    /// <see cref="LogEvent{T}.Item"/> 是草案 <see cref="LogEvent{T}.Sequence"/> 位置的挥发性项目
    /// <see cref="LogEvent{T}.DraftId"/>. 零位开始为草案的内容。
    /// </summary>
    DraftItem = 2,

    /// <summary>
    /// <see cref="LogEvent{T}.DraftId"/> 草案已关闭,其内容由 <see cref="LogEvent{T}.Count"/>取代
    /// 以序列号 <see cref="LogEvent{T}.Sequence"/>开始的条目。丢弃其项目。零数表示
    /// 草案干净地结束,没有条目。
    /// </summary>
    DraftCommitted = 3,

    /// <summary>
    /// 草案草案草案草案 <see cref="LogEvent{T}.DraftId"/> 关闭条目, 以不关闭为原因 <see cref="LogEvent{T}.AbandonReason"/>.
    /// 丢弃其项目。
    /// </summary>
    DraftAbandoned = 4,

    /// <summary>
    /// <see cref="IDurableLog{T}.TrimBefore"/> 之前丢弃的条目 <see cref="LogEvent{T}.Sequence"/>,新的
    /// 保留的第一个条目。 从该序列中读取的条目继续有效。 在它继续之前的位置上设置的读者。
    /// </summary>
    Trimmed = 5,

    /// <summary>
    /// 流已经传送了当读开始时所执行的每一个条目。 <see cref="LogEvent{T}.Sequence"/> 是
    /// 流水将这次活动一次交付给读者
    /// </summary>
    CaughtUp = 6,

    /// <summary>
    /// <see cref="IDurableLog{T}.Seal"/> 结束了日志,日志的条目在 <see cref="LogEvent{T}.Sequence"/>之前结束。这是
    /// 溪流的最后活动
    /// </summary>
    Sealed = 7,
}

/// <summary>
/// 说明为什么无条目关闭的 <see cref="IDurableLog{T}"/> 草案。
/// </summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.LogDraftAbandonReason")]
public enum LogDraftAbandonReason : byte
{
    /// <summary>
    /// 事件并不描述废弃的草稿。
    /// </summary>
    None = 0,

    /// <summary>
    /// 所有人放弃了 <see cref="IDurableLogDraft{T}.Abandon"/>的草稿。
    /// </summary>
    Abandoned = 1,

    /// <summary>
    /// 车主在未关闭之前处理完草稿。
    /// </summary>
    Disposed = 2,

    /// <summary>
    /// 日志的日记被删除了
    /// </summary>
    JournalDeleted = 3,

    /// <summary>
    /// 另一启动打开了草稿, 日志保留不保留任何承诺。 草稿只存在记忆中, 所以它
    /// 内容丢失。
    /// </summary>
    PreviousActivation = 4,

    /// <summary>
    /// 启动程序开启了草案的起草或对草案提出主张,既未保留其中的一项承诺,也未记录为何结束。
    /// </summary>
    Unknown = 5,

    /// <summary>
    /// <see cref="IDurableLog{T}.Seal"/> 在草案开放时结束了日志 。
    /// </summary>
    Sealed = 6,
}

/// <summary>
/// 描述从 <see cref="IDurableLog{T}"/>读取的条目或事件。
/// </summary>
/// <typeparam name="T">日志中条目的类型。</typeparam>
[GenerateSerializer]
[Alias("Orleans.Durable.Journaling.LogEvent`1")]
public readonly record struct LogEvent<T>
{
    /// <summary>
    /// 会有这样的事件
    /// </summary>
    [Id(0)]
    public LogEventKind Kind { get; init; }

    /// <summary>
    /// 获取条目序列编号; <see cref="LogEventKind.Reset"/> 和
    /// <see cref="LogEventKind.Trimmed"/>; <see cref="LogEventKind.CaughtUp"/> 和 <see cref="LogEventKind.Sealed"/>的位置;项目在其草案中的位置;或a的首序号
    /// 指定草稿条目 。
    /// </summary>
    [Id(1)]
    public long Sequence { get; init; }

    /// <summary>
    /// 获取 <see cref="LogEventKind.Entry"/> 的条目或 <see cref="LogEventKind.DraftItem"/>的项目草案。
    /// </summary>
    [Id(2)]
    public T? Item { get; init; }

    /// <summary>
    /// 获取事件草案的识别符号草稿。
    /// </summary>
    [Id(3)]
    public LogDraftId? DraftId { get; init; }

    /// <summary>
    /// 获取所有者传给 <see cref="IDurableLogDraft{T}.Abandon"/>的 <see cref="LogEventKind.DraftAbandoned"/>描述。
    /// </summary>
    [Id(4)]
    public string? Message { get; init; }

    /// <summary>
    /// 获取替换草稿的条目数,用于 <see cref="LogEventKind.DraftCommitted"/>。
    /// </summary>
    [Id(5)]
    public int Count { get; init; }

    /// <summary>
    /// 获取为什么没有条目的草稿关闭的原因, 对于 <see cref="LogEventKind.DraftAbandoned"/>。
    /// </summary>
    [Id(6)]
    public LogDraftAbandonReason AbandonReason { get; init; }

    /// <summary>
    /// 获取事件所属历史的时代。 以序列号传递给它, 继续读取 。
    /// </summary>
    [Id(7)]
    public long Epoch { get; init; }
}

/// <summary>
/// 记录 <see cref="IDurableLog{T}"/> 草案承诺从 <see cref="Sequence"/>开始的条目。
/// </summary>
/// <param name="DraftId">草稿的识别特征。</param>
/// <param name="Sequence">第一个承诺条目的序列号。</param>
/// <param name="Count">承诺条目的数量。</param>
public readonly record struct LogDraftCommit(LogDraftId DraftId, long Sequence, int Count);
