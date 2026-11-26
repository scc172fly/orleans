using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 交易状态存储界面
    /// </summary>
    /// <typeparam name="TState">国家类型</typeparam>
    public interface ITransactionalStateStorage<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 将权威交易状态从耐久存储中加载。
        /// </summary>
        /// <returns>
        /// 其结果是提供一份连贯的简况,其中列有目前的ETag、承诺状态和序列号。
        /// 并准备记录 序列编号顺序。
        /// </returns>
        /// <remarks>
        /// 退回的快照必须适合交易收回。
        /// 可恢复的耐久状态,不得将不兼容储存版本的数值合并。
        /// <para>
        /// 呼叫 <see cref="Store"/> 时,零后会失败,一些或要求的更改全部成为耐久的。
        /// 因此, <see cref="Load"/> 必须读取权威的持久国,并取代任何供应商-当地国
        /// 用于随后拨打的缓存 <see cref="Store"/>。在失败后,调用此操作的用户会使用此操作
        /// <see cref="Store"/> ,以便在发布另一份更新之前解决结果。
        /// </para>
        /// </remarks>
        Task<TransactionalStorageLoadResponse<TState>> Load();

        /// <summary>
        /// 商店交易状态的变化
        /// </summary>
        /// <param name="expectedETag">
        /// 最近的 <see cref="Load"/> 或成功的 <see cref="Store"/>的ETag,确定耐久性
        /// 版本要更新。
        /// </param>
        /// <param name="metadata">取代当前存储元数据的交易元数据。</param>
        /// <param name="statesToPrepare">
        /// 准备添加或替换的记录,以序列号为键。
        /// 与相同的序列编号。 A <see langword="null"/> 或空列表不添加准备记录。
        /// </param>
        /// <param name="commitUpTo">
        /// 当无Null时,通过该序列号的预准备状态推进承诺状态并
        /// 记录在序列或以下编制已过时。
        /// </param>
        /// <param name="abortAfter">
        /// 当非核时,删除待备好的记录,其序列号严格高于该数值。
        /// </param>
        /// <returns>
        /// 其结果是完成的行动所形成的持久状态的ETag。
        /// 下次更新时使用 <paramref name="expectedETag"/> 。
        /// </returns>
        /// <remarks>
        /// 执行必须使用 <paramref name="expectedETag"/> 来达成乐观的货币货币,但操作失败
        /// 而不是静默覆盖不同的耐久版本。 报告不匹配所使用的例外类型是
        /// 具体供应商。
        /// <para>
        /// 实施阶段可将一项作业分成提供方规模的原子批量。
        /// 保存可恢复性,但 <see cref="Store"/> 整个呼叫无需
        /// 原原子: 如果投出, 零, 部分或所有要求的更改可能已经可以持久, 包括
        /// 未能完成批次之前填写的批次在未完成批次中未失效的批次之前完成。因此,一项例外情形不能证明没有持久的更改
        /// 发生。调用者不得使用其缓存状态或ETag盲目重试;它必须调用 <see cref="Load"/>
        /// 并首先从权威快照中恢复过来
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
    /// 表示由交易编制但尚未承诺的国家版本。
    /// </summary>
    /// <typeparam name="TState">交易状态类型。</typeparam>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class PendingTransactionState<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 交易按本地的密序编号1,2,3,4...
        /// 如果新交易的编制与a 
        /// 此前准备的交易,替换。
        /// </summary>
        [Id(0)]
        public long SequenceId { get; set; }

        /// <summary>
        /// 全球独特的交易识别码。 
        /// </summary>
        [Id(1)]
        public string TransactionId { get; set; } = null!;

        /// <summary>
        /// 交易的逻辑时间戳 。
        /// 时标保证单调增加。
        /// </summary>
        [Id(2)]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// 交易经理知道该交易的状况
        /// 如果这是交易管理器,则无效。
        /// 用于调查交易的去向。
        /// </summary>
        [Id(3)]
        public ParticipantId TransactionManager { get; set; }

        /// <summary>
        /// 交易完成后国家状况的简况
        /// </summary>
        [Id(4)]
        public TState State { get; set; } = null!;
    }

    /// <summary>
    /// 代表从耐久储存中装载的权威性交易状态。
    /// </summary>
    /// <typeparam name="TState">交易状态类型。</typeparam>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class TransactionalStorageLoadResponse<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 初始化空状态的 <see cref="TransactionalStorageLoadResponse{TState}"/> 类的新实例。
        /// </summary>
        public TransactionalStorageLoadResponse() : this(null, new TState(), 0, new TransactionalStateMetaData(), Array.Empty<PendingTransactionState<TState>>()) { }

        /// <summary>
        /// 初始化 <see cref="TransactionalStorageLoadResponse{TState}"/> 类的新实例。
        /// </summary>
        /// <param name="etag">存储版本标识符。</param>
        /// <param name="committedState">最近承诺的状态。</param>
        /// <param name="committedSequenceId">最近承诺状态的序列号。</param>
        /// <param name="metadata">交易协议元数据</param>
        /// <param name="pendingStates">尚未承诺的已准备好的州文本。</param>
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
        /// 获取或设置最近承诺状态。
        /// </summary>
        [Id(1)]
        public TState CommittedState { get; set; }

        /// <summary>
        /// 上次交易的本地序列代号,如果无则为零
        /// </summary>
        [Id(2)]
        public long CommittedSequenceId { get; set; }

        /// <summary>
        /// 交易算法维持的额外状态,例如承诺记录
        /// </summary>
        [Id(3)]
        public TransactionalStateMetaData Metadata { get; set; }

        /// <summary>
        /// 待定国家名单,按顺序编号
        /// </summary>
        [Id(4)]
        public IReadOnlyList<PendingTransactionState<TState>> PendingStates { get; set; }
    }

    /// <summary>
    /// 元数据作为 JSON 对象存储在存储中
    /// </summary>
    [GenerateSerializer]
    [Serializable]
    public sealed class TransactionalStateMetaData
    {
        /// <summary>
        /// 获取或设置交易状态所观察到的最新逻辑时间戳。
        /// </summary>
        [Id(0)]
        public DateTime TimeStamp { get; set; } = default;

        /// <summary>
        /// 获取或建立为收回交易而保留的承付记录。
        /// </summary>
        [Id(1)]
        public Dictionary<Guid, CommitRecord> CommitRecords { get; set; } = new Dictionary<Guid, CommitRecord>();
    }

    /// <summary>
    /// 记录一次交易的登记时间和写入参与者。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class CommitRecord
    {
        /// <summary>
        /// 获取或设置交易执行时间戳。
        /// </summary>
        [Id(0)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 获取或设置交易期间所写的参与者。
        /// </summary>
        [Id(1)]
        public List<ParticipantId> WriteParticipants { get; set; } = null!;
    }
}
