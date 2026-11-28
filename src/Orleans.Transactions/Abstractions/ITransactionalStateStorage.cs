using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 事务状态的存储接口。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    public interface ITransactionalStateStorage<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 从持久化存储加载权威事务状态。
        /// </summary>
        /// <returns>
        /// 返回一个任务，其结果为一致的快照，包含当前 ETag、已提交状态及序列号、
        /// 事务元数据，以及按序列号排序的待定准备记录。
        /// </returns>
        /// <remarks>
        /// 返回的快照必须适用于事务恢复，尤其必须表示同一个
        /// 可恢复的持久化状态，不得混合来自不兼容存储版本的值。
        /// <para>
        /// 调用 <see cref="Store"/> 可能在请求的变更尚未持久化、部分持久化或全部持久化后失败。
        /// 因此，<see cref="Load"/> 必须读取权威的持久化状态，并替换提供程序在本地缓存的状态，
        /// 该缓存用于后续 <see cref="Store"/> 调用。调用方在
        /// <see cref="Store"/> 失败后使用此操作确定结果，然后再发出更新。
        /// </para>
        /// </remarks>
        Task<TransactionalStorageLoadResponse<TState>> Load();

        /// <summary>
        /// 存储事务状态变更。
        /// </summary>
        /// <param name="expectedETag">
        /// 来自最近一次 <see cref="Load"/> 或成功的 <see cref="Store"/> 的 ETag，用于标识要更新的
        /// 持久化版本。
        /// </param>
        /// <param name="metadata">用于替换当前已存储元数据的事务元数据。</param>
        /// <param name="statesToPrepare">
        /// 要添加或替换的准备记录，以序列号为键。新记录会替换
        /// 具有相同序列号的现有记录。<see langword="null"/> 或空列表表示不添加准备记录。
        /// </param>
        /// <param name="commitUpTo">
        /// 非空时，将已提交状态推进至该序列号对应的已准备状态，并使
        /// 序列号小于或等于该值的准备记录失效。
        /// </param>
        /// <param name="abortAfter">
        /// 非空时，移除序列号严格大于该值的待定准备记录。
        /// </param>
        /// <returns>
        /// 返回一个任务，其结果为操作完成后产生的持久化状态的 ETag。返回的 ETag
        /// 用作下一次更新的 <paramref name="expectedETag"/>。
        /// </returns>
        /// <remarks>
        /// 实现必须使用 <paramref name="expectedETag"/> 进行乐观并发控制，遇到版本不匹配时必须使操作失败，
        /// 而不能静默覆盖不同的持久化版本。用于报告不匹配的异常类型
        /// 由各提供程序自行定义。
        /// <para>
        /// 实现可以将操作拆分为符合提供程序容量限制的原子批次。这些批次按照
        /// 保持可恢复性的顺序执行，但并不要求 <see cref="Store"/> 调用整体具有
        /// 原子性：如果抛出异常，请求的变更可能尚未持久化、部分持久化或全部持久化，包括
        /// 失败批次之前已完成的批次中的变更。因此，异常并不能证明未发生持久化变更。
        /// 调用方不得使用缓存的状态或 ETag 盲目重试，而必须调用 <see cref="Load"/>，
        /// 首先根据权威快照进行恢复。
        /// </para>
        /// </remarks>
        Task<string> Store(
            string? expectedETag,
            TransactionalStateMetaData metadata,
            List<PendingTransactionState<TState>>? statesToPrepare,
            long? commitUpTo,
            long? abortAfter
        );
    }

    /// <summary>
    /// 表示由事务准备好但尚未提交的状态版本。
    /// </summary>
    /// <typeparam name="TState">事务状态类型。</typeparam>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class PendingTransactionState<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 为事务分配连续的本地序列号 1、2、3、4……
        /// 如果新事务在准备时使用了与某个
        /// 先前已准备事务相同的序列号，则它会替换先前的事务。
        /// </summary>
        [Id(0)]
        public long SequenceId { get; set; }

        /// <summary>
        /// 事务的全局唯一标识符。
        /// </summary>
        [Id(1)]
        public string TransactionId { get; set; } = null!;

        /// <summary>
        /// 事务的逻辑时间戳。
        /// 保证时间戳单调递增。
        /// </summary>
        [Id(2)]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// 掌握此已准备事务状态的事务管理器；
        /// 如果当前参与者就是事务管理器，则为 null。
        /// 用于在恢复期间查询事务的最终结果。
        /// </summary>
        [Id(3)]
        public ParticipantId TransactionManager { get; set; }

        /// <summary>
        /// 此事务执行完毕后的状态快照。
        /// </summary>
        [Id(4)]
        public TState State { get; set; } = null!;
    }

    /// <summary>
    /// 表示从持久化存储加载的权威事务状态。
    /// </summary>
    /// <typeparam name="TState">事务状态类型。</typeparam>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class TransactionalStorageLoadResponse<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 使用空状态初始化 <see cref="TransactionalStorageLoadResponse{TState}"/> 类的新实例。
        /// </summary>
        public TransactionalStorageLoadResponse() : this(null, new TState(), 0, new TransactionalStateMetaData(), Array.Empty<PendingTransactionState<TState>>()) { }

        /// <summary>
        /// 初始化 <see cref="TransactionalStorageLoadResponse{TState}"/> 类的新实例。
        /// </summary>
        /// <param name="etag">存储版本标识符。</param>
        /// <param name="committedState">最近一次提交的状态。</param>
        /// <param name="committedSequenceId">最近一次提交状态的序列号。</param>
        /// <param name="metadata">事务协议元数据。</param>
        /// <param name="pendingStates">尚未提交的已准备状态版本。</param>
        public TransactionalStorageLoadResponse(string? etag, TState committedState, long committedSequenceId, TransactionalStateMetaData metadata, IReadOnlyList<PendingTransactionState<TState>> pendingStates)
        {
            this.ETag = etag;
            this.CommittedState = committedState;
            this.CommittedSequenceId = committedSequenceId;
            this.Metadata = metadata;
            this.PendingStates = pendingStates;
        }

        /// <summary>
        /// 获取或设置存储版本标识符。
        /// </summary>
        [Id(0)]
        public string? ETag { get; set; }

        /// <summary>
        /// 获取或设置最近一次提交的状态。
        /// </summary>
        [Id(1)]
        public TState CommittedState { get; set; }

        /// <summary>
        /// 最后一次提交的事务的本地序列 ID；若无则为零。
        /// </summary>
        [Id(2)]
        public long CommittedSequenceId { get; set; }

        /// <summary>
        /// 事务算法维护的额外状态，例如提交记录。
        /// </summary>
        [Id(3)]
        public TransactionalStateMetaData Metadata { get; set; }

        /// <summary>
        /// 按序列 ID 排序的待定状态列表。
        /// </summary>
        [Id(4)]
        public IReadOnlyList<PendingTransactionState<TState>> PendingStates { get; set; }
    }

    /// <summary>
    /// 元数据以 JSON 对象的形式存储。
    /// </summary>
    [GenerateSerializer]
    [Serializable]
    public sealed class TransactionalStateMetaData
    {
        /// <summary>
        /// 获取或设置事务状态所观察到的最新逻辑时间戳。
        /// </summary>
        [Id(0)]
        public DateTime TimeStamp { get; set; } = default;

        /// <summary>
        /// 获取或设置为事务恢复而保留的提交记录。
        /// </summary>
        [Id(1)]
        public Dictionary<Guid, CommitRecord> CommitRecords { get; set; } = new Dictionary<Guid, CommitRecord>();
    }

    /// <summary>
    /// 记录已提交事务的时间戳和写入参与者。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class CommitRecord
    {
        /// <summary>
        /// 获取或设置事务的提交时间戳。
        /// </summary>
        [Id(0)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 获取或设置在事务期间执行过写入的参与者。
        /// </summary>
        [Id(1)]
        public List<ParticipantId> WriteParticipants { get; set; } = null!;
    }
}
