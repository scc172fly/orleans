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
    /// 接收将累积到存储批次中的事务状态变更。
    /// </summary>
    /// <typeparam name="TState">事务状态类型。</typeparam>
    public interface ITransactionalStateStorageEvents<TState> where TState : class, new()
    {
        /// <summary>
        /// 添加或替换一个已准备状态版本。
        /// </summary>
        /// <param name="sequenceNumber">分配给事务的本地序列号。</param>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timestamp">事务提交时间戳。</param>
        /// <param name="transactionManager">协调该事务的事务管理器。</param>
        /// <param name="state">事务产生的状态。</param>
        void Prepare(long sequenceNumber, Guid transactionId, DateTime timestamp, ParticipantId transactionManager, TState state);

        /// <summary>
        /// 记录指定逻辑时间戳上的一次读取。
        /// </summary>
        /// <param name="timestamp">读取操作的逻辑时间戳。</param>
        void Read(DateTime timestamp);

        /// <summary>
        /// 取消指定序列号对应的已准备状态版本，并将该版本以及其后的已准备版本排除在批次之外。
        /// </summary>
        /// <param name="sequenceNumber">要取消的序列号。</param>
        void Cancel(long sequenceNumber);

        /// <summary>
        /// 确认截至指定序列号的已提交状态。
        /// </summary>
        /// <param name="sequenceNumber">已确认的序列号。</param>
        void Confirm(long sequenceNumber);

        /// <summary>
        /// 记录已提交事务，以供恢复使用。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="timestamp">事务提交时间戳。</param>
        /// <param name="writeResources">在事务期间执行过写入的参与者。</param>
        void Commit(Guid transactionId, DateTime timestamp, List<ParticipantId> writeResources);

        /// <summary>
        /// 移除指定事务的恢复记录。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        void Collect(Guid transactionId);
    }

    /// <summary>
    /// 累积存储事件，以便成批提交给存储系统。
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    internal class StorageBatch<TState> : ITransactionalStateStorageEvents<TState>
        where TState : class, new()
    {
        // 提交、准备和中止的水位标记。
        private long confirmUpTo;
        private long cancelAbove;
        private readonly long cancelAboveStart;

        // 准备记录。
        private readonly SortedDictionary<long, PendingTransactionState<TState>> prepares;
        private readonly List<Guid> committedTransactionIds;

        // 在此批次完成后执行的后续操作。
        private readonly List<Action<bool>> followUpActions;
        private readonly List<Func<Task<bool>>> storeConditions;
        private int completionInvoked;

        // 各类事件的计数器。
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

            // 移除已被之后的确认状态取代的所有冗余准备记录。
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
