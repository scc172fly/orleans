
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions
{
    /// <summary>
    /// 每个参与者在提交协议中承担特定角色。
    /// </summary>
    internal enum CommitRole
    {
        NotYetDetermined,  // 只有收到 TA 的准备消息后才知道角色。
        ReadOnly,          // 此参与者未执行过写入。
        RemoteCommit,      // 此参与者执行过写入，但不是 TM。
        LocalCommit,       // 此参与者执行过写入，并且是 TM。
    }

    /// <summary>
    /// 每个参与者为每项事务保留的记录。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    internal class TransactionRecord<TState>
    {
        public TransactionRecord()
        {
        }

        // 此事务的唯一标识符。
        public Guid TransactionId;

        // 此事务在 TA 上启动的时间。
        public DateTime Priority;

        // 排队事务获取锁的截止时间。
        public DateTime Deadline;

        // 此参与者获取锁后必须持有该锁的最短时间。
        public TimeSpan LockTimeout;

        // 算法计算出的事务时间戳。
        public DateTime Timestamp;

        // 此事务对当前事务参与者执行的读写次数。
        public int NumberReads;
        public int NumberWrites;

        // 此事务的状态及该状态的序列号。
        public TState State = default!;
        public long SequenceNumber;
        public bool HasCopiedState;

        // 表示此事务持有排他锁。
        public bool IsExclusiveLock;

        public void AddRead()
        {
            NumberReads++;
        }
        public void AddWrite()
        {
            NumberWrites++;
        }

        public CommitRole Role;

        // 用于只读事务和本地提交。
        public TaskCompletionSource<TransactionalStatus> PromiseForTA = null!;

        // 用于本地提交和远程提交。
        public ParticipantId TransactionManager;

        // 用于本地提交。
        public List<ParticipantId> WriteParticipants = null!;
        public int WaitCount;
        public DateTime WaitingSince;

        // 用于远程提交。
        public DateTime? LastSent;
        public bool IsRestoredRemoteCommit;
        public int RecoveryPingCount;
        public bool PrepareIsPersisted;
        public TaskCompletionSource<bool>? ConfirmationResponsePromise;

        internal DateTime GetNextRemotePingAt(TimeSpan pingFrequency)
        {
            if (IsRestoredRemoteCommit && RecoveryPingCount == 0)
            {
                return DateTime.MinValue;
            }

            return LastSent!.Value + GetRemotePingDelay(pingFrequency);
        }

        internal void RecordRemotePingSent(DateTime sentAt)
        {
            LastSent = sentAt;
            if (RecoveryPingCount < int.MaxValue)
            {
                RecoveryPingCount++;
            }
        }

        private TimeSpan GetRemotePingDelay(TimeSpan pingFrequency)
        {
            if (RecoveryPingCount == 0)
            {
                return pingFrequency;
            }

            var exponent = Math.Min(RecoveryPingCount - 1, 30);
            var retryDelayTicks = TimeSpan.TicksPerSecond << exponent;
            return TimeSpan.FromTicks(Math.Min(retryDelayTicks, pingFrequency.Ticks));
        }

        /// <summary>
        /// 指示事务记录是否已做好提交准备。
        /// </summary>
        public bool ReadyToCommit
        {
            get
            {
                switch (Role)
                {
                    case CommitRole.ReadOnly:
                        return true;

                    case CommitRole.LocalCommit:
                        return WaitCount == 0; // 已收到所有“Prepared”（已准备）消息。

                    case CommitRole.RemoteCommit:
                        return
                            (ConfirmationResponsePromise != null)  // TM 已发送确认消息，正在等待响应。
                         || (NumberWrites == 0 && LastSent.HasValue);  // 此参与者未执行过写入，并已完成准备。

                    default:
                        throw new NotSupportedException($"{Role} is not a supported CommitRole.");
                }
            }
        }

        public bool IsReadOnly
        {
            get
            {
                switch (Role)
                {
                    case CommitRole.ReadOnly:
                        return true;
                    case CommitRole.LocalCommit:
                        return false;
                    case CommitRole.RemoteCommit:
                        return NumberWrites == 0;
                    default:
                        throw new NotSupportedException($"{Role} is not a supported CommitRole.");
                }
            }
        }

        public bool Batchable
        {
            get
            {
                switch (Role)
                {
                    case CommitRole.ReadOnly:
                    case CommitRole.LocalCommit:
                        return true;
                    case CommitRole.RemoteCommit:
                        return NumberWrites == 0;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        // 格式化输出，用于调试提交队列的内容。
        public override string ToString()
        {
            switch (Role)
            {
                case CommitRole.NotYetDetermined:
                    return $"ND tid={TransactionId} v{SequenceNumber}";

                case CommitRole.ReadOnly:
                    return $"RE tid={TransactionId} v{SequenceNumber}";

                case CommitRole.LocalCommit:
                    return $"LCE tid={TransactionId} v{SequenceNumber} wc={WaitCount} rtb={ReadyToCommit}";

                case CommitRole.RemoteCommit:
                    return $"RCE tid={TransactionId} v{SequenceNumber} pip={PrepareIsPersisted} ls={LastSent.HasValue} ro={IsReadOnly} rtb={ReadyToCommit} tm={TransactionManager}";

                default:
                    throw new NotSupportedException($"{Role} is not a supported CommitRole.");
            }
        }
    }
}
