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
    /// 携带事务的标识、时序信息、参与者访问情况和提交状态。
    /// </summary>
    [GenerateSerializer]
    public sealed class TransactionInfo
    {
        /// <summary>
        /// 初始化空的事务信息实例。
        /// </summary>
        public TransactionInfo()
        {
            this.Participants = new Dictionary<ParticipantId, AccessCounter>(ParticipantId.Comparer);
            this.joined = new ConcurrentQueue<TransactionInfo>();
        }

        /// <summary>
        /// 使用事务标识和调度时间戳初始化事务信息。
        /// </summary>
        /// <param name="id">唯一的事务标识符。</param>
        /// <param name="timeStamp">用于确定提交时间戳的初始因果时间戳。</param>
        /// <param name="priority">获取参与者锁时用于对事务排序的时间戳。</param>
        /// <param name="readOnly">事务是否声明为只读。</param>
        public TransactionInfo(Guid id, DateTime timeStamp, DateTime priority, bool readOnly = false) : this()
        {
            this.TransactionId = id;
            this.IsReadOnly = readOnly;
            this.TimeStamp = timeStamp;
            this.Priority = priority;
        }

        /// <summary>
        /// 使用另一实例的事务级元数据初始化事务分支。
        /// </summary>
        /// <param name="other">要复制的事务信息。</param>
        /// <remarks>
        /// 参与者访问计数、待处理调用状态和已加入的分支仍各自保存在本地实例中。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="other"/> 为 <see langword="null"/>。</exception>
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
        /// 获取事务标识符的字符串表示形式。
        /// </summary>
        public string Id => TransactionId.ToString();

        /// <summary>
        /// 获取唯一的事务标识符。
        /// </summary>
        [Id(0)]
        public Guid TransactionId { get; }

        /// <summary>
        /// 获取或设置事务的因果时间戳，该时间戳会向前推进以涵盖参与者的时间戳。
        /// </summary>
        [Id(1)]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// 获取或设置获取参与者锁时用于对事务排序的时间戳。
        /// </summary>
        [Id(2)]
        public DateTime Priority { get; set; }

        /// <summary>
        /// 获取指示事务是否声明为只读的值。
        /// </summary>
        [Id(3)]
        public bool IsReadOnly { get; }

        /// <summary>
        /// 获取或设置要求事务中止的首个序列化异常。
        /// </summary>
        [Id(4)]
        public byte[]? OriginalException { get; set; }

        /// <summary>
        /// 获取以参与者标识符为键的参与者访问计数。
        /// </summary>
        /// <remarks>写入计数为零表示仅对该参与者执行过读取。</remarks>
        [Id(5)]
        public Dictionary<ParticipantId, AccessCounter> Participants { get; }

        /// <summary>
        /// 获取指示事务委托是否投票同意提交的值。
        /// </summary>
        [Id(6)]
        public bool TryToCommit { get; internal set; } = true;

        /// <summary>
        /// 获取或设置指示参与者读取操作是否获取排他锁的值。
        /// </summary>
        [Id(7)]
        public bool UseExclusiveLock { get; set; } = false;

        [Id(8)]
        internal TimeSpan Timeout { get; set; }

        /// <summary>
        /// 获取或设置尚未完成合并处理的分支调用数量。
        /// </summary>
        [NonSerialized]
        public int PendingCalls;

        [NonSerialized]
        private readonly ConcurrentQueue<TransactionInfo> joined;

        /// <summary>
        /// 为分支调用创建事务信息，并记录该调用处于待处理状态。
        /// </summary>
        /// <returns>包含当前实例事务级元数据的新事务分支。</returns>
        public TransactionInfo Fork()
        {
            Interlocked.Increment(ref PendingCalls);
            return new TransactionInfo(this);
        }

        /// <summary>
        /// 将已完成的事务分支加入队列，等待与当前实例进行合并处理。
        /// </summary>
        /// <param name="x">要加入的已完成事务分支。</param>
        /// <exception cref="ArgumentNullException"><paramref name="x"/> 为 <see langword="null"/>。</exception>
        public void Join(TransactionInfo x)
        {
            ArgumentNullException.ThrowIfNull(x);
            joined.Enqueue(x);
        }

        /// <summary>
        /// 确定事务是否因已记录异常或仍存在未完成的分支调用而必须中止。
        /// </summary>
        /// <param name="serializer">用于反序列化已记录事务异常的序列化器。</param>
        /// <returns>
        /// 已记录的中止异常、针对未完成调用的孤立调用异常，或 <see langword="null"/>
        /// （当事务不存在必须中止的条件时）。
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// 需要反序列化已记录的异常时，<paramref name="serializer"/> 为 <see langword="null"/>。
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
        /// 将事务执行过程中抛出的第一个异常记录为序列化的事务中止异常。
        /// </summary>
        /// <param name="e">事务执行期间抛出的异常。</param>
        /// <param name="sm">用于存储异常以便随事务传播的序列化器。</param>
        /// <exception cref="ArgumentNullException">
        /// 记录第一个异常时，<paramref name="sm"/> 为 <see langword="null"/>。
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
        /// 合并所有已加入的事务分支，并更新未完成的调用计数。
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

            // 将写入计数相加。
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

            // 取最大时间戳。
            if (TimeStamp < other.TimeStamp)
                TimeStamp = other.TimeStamp;

            // 取待提交标志。
            if (TryToCommit)
                TryToCommit = other.TryToCommit;
        }

        /// <summary>
        /// 记录一次参与者读取，并在需要时推进事务时间戳。
        /// </summary>
        /// <param name="id">被读取的参与者。</param>
        /// <param name="minTime">读取之后事务必须观察到的最小时间戳。</param>
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
        /// 记录一次参与者写入，并在需要时推进事务时间戳。
        /// </summary>
        /// <param name="id">被写入的参与者。</param>
        /// <param name="minTime">写入之后事务必须观察到的最小时间戳。</param>
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
        /// 返回事务及其参与者访问情况的诊断表示形式。
        /// </summary>
        /// <returns>包含事务时序、结果状态和参与者访问计数的字符串。</returns>
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
