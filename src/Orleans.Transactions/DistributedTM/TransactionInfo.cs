using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Orleans.Serialization;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 获取交易的身份、时间、参与者的接触和进行交易。
    /// </summary>
    [GenerateSerializer]
    public sealed class TransactionInfo
    {
        /// <summary>
        /// 初始化一个空交易信息实例 。
        /// </summary>
        public TransactionInfo()
        {
            this.Participants = new Dictionary<ParticipantId, AccessCounter>(ParticipantId.Comparer);
            this.joined = new ConcurrentQueue<TransactionInfo>();
        }

        /// <summary>
        /// 以其身份和时间安排时间戳初始化交易信息。
        /// </summary>
        /// <param name="id">独特的交易识别特征。</param>
        /// <param name="timeStamp">用于确定承诺时间戳的初始因果时间戳。</param>
        /// <param name="priority">获取参与人锁定时用于命令交易的时间戳。</param>
        /// <param name="readOnly">是否只宣布交易为只读。</param>
        public TransactionInfo(Guid id, DateTime timeStamp, DateTime priority, bool readOnly = false) : this()
        {
            this.TransactionId = id;
            this.IsReadOnly = readOnly;
            this.TimeStamp = timeStamp;
            this.Priority = priority;
        }

        /// <summary>
        /// 以另一个实例的全交易元数据初始化交易分支。
        /// </summary>
        /// <param name="other">要复制的交易信息。</param>
        /// <remarks>
        /// 参加调查者对进入情况进行清点,待查州和加入的分支机构对每个案件来说仍然是地方性的。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> 是 <see langword="null"/>.</exception>
        public TransactionInfo(TransactionInfo other) : this()
        {
            ArgumentNullException.ThrowIfNull(other);
            this.TransactionId = other.TransactionId;
            this.TryToCommit = other.TryToCommit;
            this.IsReadOnly = other.IsReadOnly;
            this.UseExclusiveLock = other.UseExclusiveLock;
            this.TimeStamp = other.TimeStamp;
            this.Priority = other.Priority;
            this.Timeout = other.Timeout;
        }

        /// <summary>
        /// 获取交易标识符的字符串表示。
        /// </summary>
        public string Id => TransactionId.ToString();

        /// <summary>
        /// 获得唯一的交易识别特征。
        /// </summary>
        [Id(0)]
        public Guid TransactionId { get; }

        /// <summary>
        /// 获取或设置交易的因果时间戳,预支时戳包括参与者的时间戳。
        /// </summary>
        [Id(1)]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// 获取参与者锁定时, 获取或设置用于命令交易的时间戳 。
        /// </summary>
        [Id(2)]
        public DateTime Priority { get; set; }

        /// <summary>
        /// 获得一个值,显示是否只宣布只读交易。
        /// </summary>
        [Id(3)]
        public bool IsReadOnly { get; }

        /// <summary>
        /// 获取或设置要求交易中止的第一个序列化例外。
        /// </summary>
        [Id(4)]
        public byte[]? OriginalException { get; set; }

        /// <summary>
        /// 获得参与者识别符号所输入的参与者访问数。
        /// </summary>
        /// <remarks>写数为零表示只读取了参与人。</remarks>
        [Id(5)]
        public Dictionary<ParticipantId, AccessCounter> Participants { get; }

        /// <summary>
        /// 获得一个显示交易代表是否投票承诺的值。
        /// </summary>
        [Id(6)]
        public bool TryToCommit { get; internal set; } = true;

        /// <summary>
        /// 获取或设定一个值,显示参与者是否读取了独家锁。
        /// </summary>
        [Id(7)]
        public bool UseExclusiveLock { get; set; } = false;

        [Id(8)]
        internal TimeSpan Timeout { get; set; }

        /// <summary>
        /// 获取或设置尚未调节的打到的打字电话数量。
        /// </summary>
        [NonSerialized]
        public int PendingCalls;

        [NonSerialized]
        private readonly ConcurrentQueue<TransactionInfo> joined;

        /// <summary>
        /// 为预先通知创建交易信息,记录电话待发。
        /// </summary>
        /// <returns>一个新的交易分支,包含来自此实例的全交易元数据。</returns>
        public TransactionInfo Fork()
        {
            Interlocked.Increment(ref PendingCalls);
            return new TransactionInfo(this);
        }

        /// <summary>
        /// 将一个已完成的交易分支与此实例核对。
        /// </summary>
        /// <param name="x">已完成的交易事务组加入。</param>
        /// <exception cref="ArgumentNullException"><paramref name="x"/> 是 <see langword="null"/>.</exception>
        public void Join(TransactionInfo x)
        {
            ArgumentNullException.ThrowIfNull(x);
            joined.Enqueue(x);
        }

        /// <summary>
        /// 确定交易是否因为记录了例外或被提前通知的电话仍然未结而必须中止。
        /// </summary>
        /// <param name="serializer">用于解除已记录的交易例外的序列器。</param>
        /// <returns>
        /// 记录的堕胎例外、未结电话的致孤电话例外,或 <see langword="null"/>
        /// 当交易没有强制性中止条件时。
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="serializer"/> 是指记录中的例外情况需要取消机能时的 <see langword="null"/> 。
        /// </exception>
        public OrleansTransactionAbortedException? MustAbort(Serializer<OrleansTransactionAbortedException> serializer)
        {
            if (OriginalException != null)
            {
                ArgumentNullException.ThrowIfNull(serializer);
                return serializer.Deserialize(OriginalException);
            }
            else if (PendingCalls != 0)
            {
                return new OrleansOrphanCallException(TransactionId.ToString(), PendingCalls);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 将交易执行引起的第一个例外记录为系列交易中止例外。
        /// </summary>
        /// <param name="e">交易执行期间提出的例外。</param>
        /// <param name="sm">用于存储交易传播例外的序列器。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sm"/> 记录第一个例外时为 <see langword="null"/> 。
        /// </exception>
        public void RecordException(Exception e, Serializer<OrleansTransactionAbortedException> sm)
        {
            if (OriginalException == null)
            {
                ArgumentNullException.ThrowIfNull(sm);
                var exception = (e as OrleansTransactionAbortedException)
                    ?? new OrleansTransactionAbortedException(TransactionId.ToString(), e);

                OriginalException = sm.SerializeToArray(exception);
            }
        }

        /// <summary>
        /// 合并所有合并的交易分支并更新未清的调用计数。
        /// </summary>
        public void ReconcilePending()
        {
            TransactionInfo? transactionInfo;
            while (this.joined.TryDequeue(out transactionInfo))
            {
                Union(transactionInfo);
                PendingCalls--;
            }
        }

        private void Union(TransactionInfo other)
        {
            if (OriginalException == null)
            {
                OriginalException = other.OriginalException;
            }

            // 记笔数总和
            foreach (KeyValuePair<ParticipantId, AccessCounter> participant in other.Participants)
            {
                if (!this.Participants.TryGetValue(participant.Key, out var existing))
                {
                    this.Participants[participant.Key] = participant.Value;
                }
                else
                {
                    this.Participants[participant.Key] = existing + participant.Value;
                }
            }

            // 使用最大时间戳
            if (TimeStamp < other.TimeStamp)
                TimeStamp = other.TimeStamp;

            // 执行待悬挂的国旗
            if (TryToCommit)
                TryToCommit = other.TryToCommit;
        }

        /// <summary>
        /// 参与人在需要时读取并预发交易时间戳的记录。
        /// </summary>
        /// <param name="id">被宣读的与会者。</param>
        /// <param name="minTime">读后交易必须遵守的最低时间戳。</param>
        public void RecordRead(ParticipantId id, DateTime minTime)
        {
            this.Participants.TryGetValue(id, out AccessCounter count);

            count.Reads++;

            this.Participants[id] = count;

            if (minTime > TimeStamp)
            {
                TimeStamp = minTime;
            }
        }

        /// <summary>
        /// 参与人必要时记录并预发交易时间戳。
        /// </summary>
        /// <param name="id">参加方写了那封信</param>
        /// <param name="minTime">写后交易必须遵守的最小时间戳。</param>
        public void RecordWrite(ParticipantId id, DateTime minTime)
        {
            this.Participants.TryGetValue(id, out AccessCounter count);

            count.Writes++;

            this.Participants[id] = count;

            if (minTime > TimeStamp)
            {
                TimeStamp = minTime;
            }
        }

        /// <summary>
        /// 返回交易及其参与者存取的诊断性表示。
        /// </summary>
        /// <returns>包含交易时间、结果状态和参与者访问量的字符串。</returns>
        public override string ToString()
        {
            return string.Join("",
                $"{TransactionId} {TimeStamp:o}",
                (IsReadOnly ? " RO" : ""),
                (TryToCommit ? " Committing" : ""),
                (OriginalException != null ? " Aborting" : ""),
                $" {{{string.Join(" ", this.Participants.Select(kvp => $"{kvp.Key}:{kvp.Value.Reads},{kvp.Value.Writes}"))}}}"
            );
        }
    }
}
