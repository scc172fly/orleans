
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions
{
    /// <summary>
    /// 每一参与方在承诺议定书中发挥特殊作用
    /// </summary>
    internal enum CommitRole
    {
        NotYetDetermined,  // 只有在从TA收到预写信息时才知道自己的角色
        ReadOnly,          // 本参与人未写
        RemoteCommit,      // 以物配主者已写过,但非他所意欲的。
        LocalCommit,       // 以物配主者曾写作,故是TM
    }

    /// <summary>
    /// 每一参与人保存每项交易的记录
    /// </summary>
    /// <typeparam name="TState">国家类型</typeparam>
    internal class TransactionRecord<TState>
    {
        public TransactionRecord()
        {
        }

        // 此交易的一个独有标识符
        public Guid TransactionId;

        // 交易开始于TA时
        public DateTime Priority;

        // 获得锁定的队列交易的截止时间
        public DateTime Deadline;

        // 获得锁后该参与者必须保留锁的最小时间
        public TimeSpan LockTimeout;

        // 算法计算的交易时间戳
        public DateTime Timestamp;

        // 读数和写数,即交易是该交易参与者完成的
        public int NumberReads;
        public int NumberWrites;

        // 交易的状态和顺序号
        public TState State = default!;
        public long SequenceNumber;
        public bool HasCopiedState;

        // 表明这一交易持有独家锁
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

        // 用于只读和本地承诺
        public TaskCompletionSource<TransactionalStatus> PromiseForTA = null!;

        // 用于本地和远程使用
        public ParticipantId TransactionManager;

        // 用于本地承诺
        public List<ParticipantId> WriteParticipants = null!;
        public int WaitCount;
        public DateTime WaitingSince;

        // 用于远程连接
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
        /// 表明交易记录是否准备进行
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
                        return WaitCount == 0; // 收到全部“ 准备” 信件

                    case CommitRole.RemoteCommit:
                        return
                            (ConfirmationResponsePromise != null)  // TM已经发出确认,正在等待答复
                         || (NumberWrites == 0 && LastSent.HasValue);  // 本与会者未写作并完成准备工作

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

        // 用于调试 $% 1 格式化
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
