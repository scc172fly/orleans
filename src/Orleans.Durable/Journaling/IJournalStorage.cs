using System.Buffers;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 提供日志存储。
/// </summary>
/// <remarks>
/// <para>
/// 日志是由日志段组成的有序序列。每次成功的 <see cref="AppendAsync"/> 向末尾添加一个日志段，
/// 每次成功的 <see cref="ReplaceAsync"/> 将整个日志替换为一个日志段。日志是日志状态的
/// 权威数据来源，因此每种实现均提供以下保证。
/// </para>
/// <para>
/// 调用方在通过同一个存储实例开始下一项操作前等待上一项操作完成。不同实例
/// 通过存储提供程序的写入前置条件进行协调。
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>全有或全无的写入。</b> <see cref="AppendAsync"/> 和 <see cref="ReplaceAsync"/> 使整个日志段同时可见，
/// 或者完全不可见。在进程或存储崩溃、超时、
/// 前置条件冲突或提供程序内部重试后，成功的读取仍观察到完整日志段。提供程序跨多个记录或
/// 请求存储的日志段，通过一次原子发布步骤变为可见。
/// </description></item>
/// <item><description>
/// <b>超限使整个调用失败。</b> 如果日志段超过提供程序限制，例如最大请求、块、
/// 事务或值大小，则调用以异常失败，该日志段的任何部分均不会变为可见。
/// </description></item>
/// <item><description>
/// <b>失败结果。</b> 明确被拒绝的写入（如本地检测到超限或输入无效）会保留
/// 之前的内容。请求结果不确定时（如超时或响应丢失），日志段要么完整写入，
/// 要么完全未写入。原始请求可能已提交，而重试却报告前置条件冲突，因此恢复过程
/// 即使在最终异常为 <see cref="Orleans.Storage.InconsistentStateException"/> 时也会判定实际结果。
/// 任何写入失败后，日志状态管理器均隔离自身，并通过重新读取恢复状态。
/// </description></item>
/// <item><description>
/// <b>完整、有序的读取。</b> 成功的 <see cref="ReadAsync"/> 返回日志某个版本的内容：
/// 包含截至该版本写入的每个完整日志段，按写入顺序排列，不包含任何后续写入的内容。调用方丢弃
/// 失败读取所投递的数据，并从头恢复。
/// </description></item>
/// <item><description>
/// <b>写入前置条件。</b> 每个存储实例持有由首次成功的
/// <see cref="ReadAsync"/> 或创建操作建立的写入前置条件（包括观察到日志不存在的情况），并由自身写入推进。<see cref="AppendAsync"/>、
/// <see cref="ReplaceAsync"/> 和 <see cref="DeleteAsync"/> 仅在日志仍处于该版本时成功，因此
/// 在观察到日志不存在之后写入，要求日志仍不存在。否则抛出
/// <see cref="Orleans.Storage.InconsistentStateException"/>；每次冲突请求均保持存储不变。成功的写入（包括使用
/// <see cref="CreateIfNotExistsAsync"/> 创建日志）将前置条件推进到其产生的版本。调用方在通过实例写入之前读取
/// 或创建日志；通过既未读取也未创建日志的实例写入会抛出
/// <see cref="InvalidOperationException"/>。元数据操作从不建立或推进前置条件，仅更新元数据的操作
/// 也从不改变日志内容。
/// </description></item>
/// </list>
/// <para>
/// 后续读取要求日志符合实例已经建立的前置条件，若内容变更则抛出
/// <see cref="Orleans.Storage.InconsistentStateException"/>。因此，调用方可为历史查询或压实而重新读取自身的
/// 日志，同时保留写入前置条件。冲突后的恢复使用新的
/// 存储实例。
/// </para>
/// </remarks>
public interface IJournalStorage
{
    /// <summary>
    /// 读取属于此实例的所有日志数据，并发送给 <paramref name="consumer"/>。
    /// </summary>
    /// <remarks>
    /// 读取完成时，实现必须通过传递一个以下形式的对象来通知 <paramref name="consumer"/>：
    /// <see cref="JournalBufferReader"/>，其 <see cref="JournalBufferReader.IsCompleted"/> 设置为 <see langword="true"/>。
    /// 每次调用都必须传入描述正在读取的日志文件的元数据。如果存储没有元数据，
    /// 则传入 <see langword="null"/> 或 <see cref="JournalMetadata.Empty"/>。同一次读取过程中传入的元数据在每次调用时必须具有相同的
    /// <see cref="IJournalMetadata.FormatKey"/> 值。
    /// 数据是日志某一版本的完整内容，日志段按写入顺序排列。读取到的版本（包括
    /// 日志不存在的情况）建立未读取实例的写入前置条件。后续读取验证该前置条件，
    /// 如果其他写入方改变了内容，则抛出 <see cref="Orleans.Storage.InconsistentStateException"/>。
    /// </remarks>
    /// <param name="consumer">有序原始日志数据的消费者。数据块边界不等同于日志条目边界。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
    ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken);

    /// <summary>
    /// 如果此日志存储实例尚不存在，则创建它。
    /// </summary>
    /// <remarks>
    /// 初始元数据仅在创建存储实例时应用。如果日志已经
    /// 由写入创建，则此方法返回 <see langword="false"/>，并且不更新元数据。
    /// 创建日志将空日志设为此实例的写入前置条件；发现现有日志则保持
    /// 前置条件不变。
    /// </remarks>
    /// <param name="metadata">初始的调用方拥有的元数据属性。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns><see langword="true"/> 表示已创建存储；否则为 <see langword="false"/>。</returns>
    ValueTask<bool> CreateIfNotExistsAsync(
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(IJournalStorage)} implementation does not support journal storage metadata operations.");

    /// <summary>
    /// 获取此日志存储实例的元数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>元数据；如果存储实例不存在，则为 <see langword="null"/>。</returns>
    ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(IJournalStorage)} implementation does not support journal storage metadata operations.");

    /// <summary>
    /// 有条件地更新调用方拥有的元数据属性。
    /// </summary>
    /// <remarks>
    /// 实现依据当前元数据原子地应用更新。当
    /// <paramref name="expectedETag"/> 不为 <see langword="null"/> 时，支持 ETag 的提供程序
    /// 必须仅在当前元数据 ETag 与其匹配时应用更新。提供程序拥有的元数据
    /// 必须保留。
    /// </remarks>
    /// <param name="set">要设置的元数据属性。</param>
    /// <param name="remove">要移除的元数据属性。</param>
    /// <param name="expectedETag">预期的元数据 ETag；使用 <see langword="null"/> 表示无条件更新。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>如果更新已应用或未产生变更，则返回当前元数据；否则为 <see langword="null"/>。</returns>
    ValueTask<IJournalMetadata?> UpdateMetadataAsync(
        IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null,
        string? expectedETag = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"{nameof(IJournalStorage)} implementation does not support journal storage metadata operations.");

    /// <summary>
    /// 使用提供的值原子地替换日志。
    /// </summary>
    /// <remarks>
    /// 整个值成为日志唯一的日志段，否则日志保持原有内容。如果值超过
    /// 提供程序限制，则调用失败。实现会抛出 <see cref="Orleans.Storage.InconsistentStateException"/>，条件是
    /// 日志不再符合此实例的写入前置条件；并会抛出 <see cref="InvalidOperationException"/>，条件是此
    /// 实例既未读取也未创建日志。
    /// </remarks>
    /// <param name="value">要写入的已编码日志字节。返回的任务完成后，存储提供程序不得继续持有此缓冲区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
    ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken);

    /// <summary>
    /// 将提供的日志段原子追加到日志。
    /// </summary>
    /// <remarks>
    /// 整个日志段在所有此前的日志段之后变为可见，或者完全不可见。如果日志段超过提供程序
    /// 限制，则调用失败。实现会抛出 <see cref="Orleans.Storage.InconsistentStateException"/>，条件是日志
    /// 不再符合此实例的写入前置条件；并会抛出 <see cref="InvalidOperationException"/>，条件是此实例
    /// 既未读取也未创建日志。日志段必须至少包含一个字节。
    /// </remarks>
    /// <param name="value">要追加的已编码日志字节。返回的任务完成后，存储提供程序不得继续持有此缓冲区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException">日志段为空。</exception>
    ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken);

    /// <summary>
    /// 原子地删除日志。
    /// </summary>
    /// <remarks>
    /// 当日志不再符合此实例的写入前置条件时，实现抛出 <see cref="Orleans.Storage.InconsistentStateException"/>；
    /// 当此实例既未读取也未创建日志时，则抛出 <see cref="InvalidOperationException"/>；
    /// 删除成功后，前置条件为日志不存在。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
    ValueTask DeleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取一个值，指示是否已请求压实。
    /// </summary>
    bool IsCompactionRequested { get; }
}

/// <summary>
/// 创建日志存储。
/// </summary>
public interface IJournalStorageProvider
{
    /// <summary>
    /// 为指定的日志 ID 创建日志存储。
    /// </summary>
    /// <param name="journalId">日志 ID。</param>
    /// <returns>日志存储实例。</returns>
    IJournalStorage CreateStorage(JournalId journalId);
}
