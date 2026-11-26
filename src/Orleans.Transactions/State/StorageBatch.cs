using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 接收累积到储存批次的交易状态变化。
    /// </summary>
    /// <typeparam name="TState">交易状态类型。</typeparam>
    public interface ITransactionalStateStorageEvents<TState> where TState : class, new()
    {
        /// <summary>
        /// 添加或替换已备好的国家版本。
        /// </summary>
        /// <param name="sequenceNumber">交易的本地序列编号 。</param>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timestamp">交易承诺时间戳。</param>
        /// <param name="transactionManager">交易经理协调交易。</param>
        /// <param name="state">交易产生的国家。</param>
        void Prepare(long sequenceNumber, Guid transactionId, DateTime timestamp, ParticipantId transactionManager, TState state);

        /// <summary>
        /// a 记录在指定的逻辑时间戳上读取。
        /// </summary>
        /// <param name="timestamp">读取的逻辑时间戳。</param>
        void Read(DateTime timestamp);

        /// <summary>
        /// 取消带有指定序列号的预发状态版本,并排除该版本和后来编印的版本。
        /// </summary>
        /// <param name="sequenceNumber">要取消的序列号 。</param>
        void Cancel(long sequenceNumber);

        /// <summary>
        /// 确认承诺通过指定序列号表明状态。
        /// </summary>
        /// <param name="sequenceNumber">确定序列号</param>
        void Confirm(long sequenceNumber);

        /// <summary>
        /// 记录承诺的收回交易。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="timestamp">交易承诺时间戳。</param>
        /// <param name="writeResources">交易期间写作的参与者。</param>
        void Commit(Guid transactionId, DateTime timestamp, List<ParticipantId> writeResources);

        /// <summary>
        /// 删除指定交易的回收记录。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        void Collect(Guid transactionId);
    }

    /// <summary>
    /// 积累储存事件,因为将之分批存放。
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    internal class StorageBatch<TState> : ITransactionalStateStorageEvents<TState>
        where TState : class, new()
    {
        // 用于承诺、准备、中止的水印
        private long confirmUpTo;
        private long cancelAbove;
        private readonly long cancelAboveStart;

        // 编制记录
        private readonly SortedDictionary<long, PendingTransactionState<TState>> prepares;
        private readonly List<Guid> committedTransactionIds;

        // 后续行动,待这批货物完成后执行
        private readonly List<Action<bool>> followUpActions;
        private readonly List<Func<Task<bool>>> storeConditions;
        private int completionInvoked;

        // 每种事件类型的反对数
        private int total = 0;
        private int prepare = 0;
        private int read = 0;
        private int commit = 0;
        private int confirm = 0;
        private int collect = 0;
        private int cancel = 0;

        public TransactionalStateMetaData MetaData { get; private set; }

        public string? ETag { get; set; }

        public int BatchSize => total;

        public int CommitCount => commit;

        public ImmutableArray<Guid> CommittedTransactionIds => [.. committedTransactionIds];

        public override string ToString()
        {
            return $"batchsize={total} [{read}r {prepare}p {commit}c {confirm}cf {collect}cl {cancel}cc]";
        }

        public StorageBatch(TransactionalStateMetaData metaData, string? etag, long confirmUpTo, long cancelAbove)
        {
            this.MetaData = metaData ?? throw new ArgumentNullException(nameof(metaData));
            this.ETag = etag;
            this.confirmUpTo = confirmUpTo;
            this.cancelAbove = cancelAbove;
            this.cancelAboveStart = cancelAbove;
            this.followUpActions = new List<Action<bool>>();
            this.storeConditions = new List<Func<Task<bool>>>();
            this.prepares = new SortedDictionary<long, PendingTransactionState<TState>>();
            this.committedTransactionIds = new List<Guid>();
        }

        public StorageBatch(StorageBatch<TState> previous)
            : this(previous.MetaData, previous.ETag, previous.confirmUpTo, previous.cancelAbove)
        {
        }

        public StorageBatch(TransactionalStorageLoadResponse<TState> loadresponse)
            : this(loadresponse.Metadata, loadresponse.ETag, loadresponse.CommittedSequenceId, loadresponse.PendingStates.LastOrDefault()?.SequenceId ?? loadresponse.CommittedSequenceId)
        {
        }

        public async Task<string> Store(ITransactionalStateStorage<TState> storage)
        {
            List<PendingTransactionState<TState>> list = this.prepares.Values.ToList();
            return await storage.Store(ETag, this.MetaData, list,
                (confirm > 0) ? confirmUpTo : (long?)null,
                (cancelAbove < cancelAboveStart) ? cancelAbove : (long?)null);
        }

        public void Complete(bool success)
        {
            if (Interlocked.Exchange(ref this.completionInvoked, 1) != 0)
            {
                return;
            }

            foreach (var action in followUpActions)
            {
                action(success);
            }
        }

        public void Read(DateTime timestamp)
        {
            read++;
            total++;

            if (MetaData.TimeStamp < timestamp)
            {
                MetaData.TimeStamp = timestamp;
            }
        }

        public void Prepare(long sequenceNumber, Guid transactionId, DateTime timestamp,
          ParticipantId transactionManager, TState state)
        {
            prepare++;
            total++;

            if (MetaData.TimeStamp < timestamp)
                MetaData.TimeStamp = timestamp;

            this.prepares[sequenceNumber] = new PendingTransactionState<TState>
            {
                SequenceId = sequenceNumber,
                TransactionId = transactionId.ToString(),
                TimeStamp = timestamp,
                TransactionManager = transactionManager,
                State = state
            };

            if (cancelAbove < sequenceNumber)
            {
                cancelAbove = sequenceNumber;
            }
        }

        public void Cancel(long sequenceNumber)
        {
            cancel++;
            total++;

            this.prepares.Remove(sequenceNumber);

            if (cancelAbove > sequenceNumber - 1)
            {
                cancelAbove = sequenceNumber - 1;
            }
        }

        public void Confirm(long sequenceNumber)
        {
            confirm++;
            total++;

            confirmUpTo = sequenceNumber;

            // 删除所有多余的备案记录, 以后确认状态取代
            while (true)
            {
                long? first = this.prepares.Values.FirstOrDefault()?.SequenceId;

                if (first.HasValue && first < confirmUpTo)
                {
                    this.prepares.Remove(first.Value);
                }
                else
                {
                    break;
                }
            }
        }

        public void Commit(Guid transactionId, DateTime timestamp, List<ParticipantId> WriteParticipants)
        {
            commit++;
            total++;
            committedTransactionIds.Add(transactionId);

            MetaData.CommitRecords.Add(transactionId, new CommitRecord()
            {
                Timestamp = timestamp,
                WriteParticipants = WriteParticipants
            });
        }

        public void Collect(Guid transactionId)
        {
            collect++;
            total++;

            MetaData.CommitRecords.Remove(transactionId);
        }

        public void FollowUpAction(Action<bool> action)
        {
            followUpActions.Add(action);
        }

        public void AddStorePreCondition(Func<Task<bool>> action)
        {
            this.storeConditions.Add(action);
        }

        public async ValueTask<bool> CheckStorePreConditions()
        {
            if (this.storeConditions.Count == 0)
                return true;

            bool[] results = await Task.WhenAll(this.storeConditions.Select(a => a.Invoke()));
            return results.All(b => b);
        }
    }
}
