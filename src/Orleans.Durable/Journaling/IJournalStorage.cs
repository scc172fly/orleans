using System.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供日记存储。
/// </summary>
/// <remarks>
/// <para>
/// 日记是各段的顺序。每个成功 <see cref="AppendAsync"/> 都为结尾添加一个段,
/// 并且每个成功 <see cref="ReplaceAsync"/> 用一个段取代整个日记。该日记是源
/// 以事实真相记录为日记,因此,每项执行都提供以下保障。
/// </para>
/// <para>
/// 呼叫者等待每次操作,然后通过同一个存储实例启动另一个操作。
/// 通过存储供应商的写作先决条件进行协调。
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>全部或无一物都写。</b> <see cref="AppendAsync"/> 和 <see cref="ReplaceAsync"/> 使整个段可见
/// 成功读取在进程或存储崩溃、超时、 a/ 时间后观察整段
/// 供应商储存在多个记录或
/// 原子出版步骤通过一个原子出版物步骤可以看出要求。
/// </description></item>
/// <item><description>
/// <b>限制不及全调。</b> 一个超过供应商限额的部分,例如最大请求、块、
/// 交易或价值大小, 取消调用, 但有一个例外, 并且没有任何一项能看出来 。
/// </description></item>
/// <item><description>
/// <b>失败结果。</b> 明确拒绝的写作, 如本地检测到的限制值或无效输入, 保存
/// 在请求结果不确定,如超时或答复丢失等结果不明后,这部分是:
/// 重试可报告在最初请求被承诺后发生前期冲突,因此,恢复
/// 即使最后例外是 <see cref="Orleans.Storage.InconsistentStateException"/>,也决定结果。
/// 国家管理者在写作失败后,
/// </description></item>
/// <item><description>
/// <b>整个, 订为。</b> 成功 <see cref="ReadAsync"/> 返回该日记的一个版本的内容:
/// 每一个部分都写到版本,完整和按文字顺序, 以后没有任何文字。
/// 数据由失败的读取和从开始恢复而交付。
/// </description></item>
/// <item><description>
/// <b>写先决条件。</b> 每个储存机构都持有其首次成功确定的写先决条件
/// <see cref="ReadAsync"/> 或创造,包括观察到的缺席, 由它自己的写作推进。 <see cref="AppendAsync"/>,
/// <see cref="ReplaceAsync"/>和 <see cref="DeleteAsync"/> 只有在日记仍然在版本中时才成功,因此
/// 观察缺月日日记后写时, 要求日记仍然缺席。 否则他们丢弃
/// <see cref="Orleans.Storage.InconsistentStateException"/>;每次相互冲突的请求都保留储存,没有改变。成功写入,包括创建
/// 带有 <see cref="CreateIfNotExistsAsync"/>的日记,预提前其所制作版本的先决条件。
/// 日记,或创建日记,在通过实例书写之前:通过既未丢弃又未丢弃的事例书写
/// <see cref="InvalidOperationException"/>.元数据运行从不建立或移动先决条件,仅使用元数据
/// 更新从不改变日记的内容 。
/// </description></item>
/// </list>
/// <para>
/// 之后的书写要求日记与案件既定的先决条件相符,并投出
/// <see cref="Orleans.Storage.InconsistentStateException"/> 内容更改。 因此,打电话者可以重新阅读自己的
/// 冲突后恢复使用新的
/// 存储实例。
/// </para>
/// </remarks>
public interface IJournalStorage
{
    /// <summary>
    /// 读取属于此实例的所有日记数据并将其发送到 <paramref name="consumer"/>。
    /// </summary>
    /// <remarks>
    /// 履行必须在读数完成时通知 <paramref name="consumer"/> ,
    /// <see cref="JournalBufferReader"/> 与 <see cref="JournalBufferReader.IsCompleted"/> 设置至 <see langword="true"/>.
    /// 每个通话都必须通过描述正在读取的日记文件的元数据。 如果存储没有元数据,
    /// 过 <see langword="null"/> 或 <see cref="JournalMetadata.Empty"/>。一读取时通过的元数据必须具有相同的
    /// 每一个电话的<see cref="IJournalMetadata.FormatKey"/> 值。
    /// 数据是一份日记的完整版本内容,其中各部分按写法顺序排列。
    /// 缺勤日记, 设定未读实例的写作先决条件。 随后读取该前提条件
    /// 如果另一个作家更改了内容, 则扔出 <see cref="Orleans.Storage.InconsistentStateException"/> 。
    /// </remarks>
    /// <param name="consumer">原始日记数据订购的消费者。块边界不是日报进入边界。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 代表操作。</returns>
    ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken);

    /// <summary>
    /// 如果日记存储实例不存在, 创建此例 。
    /// </summary>
    /// <remarks>
    /// 初始元数据仅在创建存储实例时才应用。如果日记是
    /// 已经通过写入创建了, 此方法返回 <see langword="false"/> , 不更新元数据 。
    /// 创建日记时将空日记变成此事件写作的先决条件;找到已有日记
    /// 先决条件未改变。
    /// </remarks>
    /// <param name="metadata">初始呼叫者拥有的元数据属性。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns><see langword="true"/> 如果创建了储存;否则, <see langword="false"/>。</returns>
    ValueTask<bool> CreateIfNotExistsAsync(
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(IJournalStorage)} implementation does not support journal storage metadata operations.");

    /// <summary>
    /// 为此日记存储实例获取元数据 。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>元数据,或 <see langword="null"/> ,如果存储实例不存在的话。</returns>
    ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(IJournalStorage)} implementation does not support journal storage metadata operations.");

    /// <summary>
    /// 有条件更新呼叫者拥有的元数据属性。
    /// </summary>
    /// <remarks>
    /// 执行时对当前元数据原子更新。
    /// <paramref name="expectedETag"/> 不是 <see langword="null"/>,是支持ETags的提供者
    /// 只有在当前元数据ETag 匹配时才必须应用更新。 提供者拥有的元数据
    /// 必须加以保护。
    /// </remarks>
    /// <param name="set">要设置的元数据属性 。</param>
    /// <param name="remove">要删除的元数据属性。</param>
    /// <param name="expectedETag">预期的元数据ETag或 <see langword="null"/> 无条件更新。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>如果更新应用或未作改动,则当前的元数据;否则, <see langword="null"/>。</returns>
    ValueTask<IJournalMetadata?> UpdateMetadataAsync(
        IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null,
        string? expectedETag = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(IJournalStorage)} implementation does not support journal storage metadata operations.");

    /// <summary>
    /// 以原子法将日记替换为所提供的值。
    /// </summary>
    /// <remarks>
    /// 整个值成为日记唯一的部分,或者日记保留其先前的内容。超过
    /// 服务供应商限制失败了调用。 执行丢弃 <see cref="Orleans.Storage.InconsistentStateException"/> 时
    /// 此例的写入先决条件不再包含日记, 此时则 <see cref="InvalidOperationException"/>
    /// 实例既不读也没有创建日记 。
    /// </remarks>
    /// <param name="value">要写入的编码日记字节。 存储提供者在返回的任务完成后不能保留此缓冲 。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 代表操作。</returns>
    ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken);

    /// <summary>
    /// 将提供的部分原时附加到日记上。
    /// </summary>
    /// <remarks>
    /// 整个段段在以往的每个段之后都可见,或者其中没有一个是看不到的。
    /// 无法调用限制。 当日记时, 执行会丢弃 <see cref="Orleans.Storage.InconsistentStateException"/>
    /// 不再属于此例的写作先决条件, 且在此例已存在时 <see cref="InvalidOperationException"/>
    /// 既不读也不创建日记。该部分必须包含至少一个字节。
    /// </remarks>
    /// <param name="value">编码的日记分节将附加。存储提供者在返回的任务完成后不得保留此缓冲 。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 代表操作。</returns>
    /// <exception cref="ArgumentOutOfRangeException">平段为空。</exception>
    ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken);

    /// <summary>
    /// 原子删除日记。
    /// </summary>
    /// <remarks>
    /// 当日记不再在此时, 执行会丢弃 <see cref="Orleans.Storage.InconsistentStateException"/>
    /// 案例的写作先决条件,以及 <see cref="InvalidOperationException"/> 既未读过,也未读过,
    /// 创建日记。 成功删除后, 先决条件是缺少日记 。
    /// </remarks>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 代表操作。</returns>
    ValueTask DeleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获得一个值,显示是否请求压缩。
    /// </summary>
    bool IsCompactionRequested { get; }
}

/// <summary>
/// 创建日记存储。
/// </summary>
public interface IJournalStorageProvider
{
    /// <summary>
    /// 为所提供的日记 id 创建日记存储。
    /// </summary>
    /// <param name="journalId">日记id。</param>
    /// <returns>日记存储实例。</returns>
    IJournalStorage CreateStorage(JournalId journalId);
}
