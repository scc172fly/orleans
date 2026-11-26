using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.ConsistentRing
{
    /// <summary>
    /// 我们用“后向/时时针”的定义来分配环上的责任。 
    /// E.g.在结点 {5,10,15} 的环中,关键7的责任是 10 (节点负责前一个范围)。 
    /// 后向/时时制办法与许多重叠一致, e.g.,和弦,卡珊德拉等
    /// 注: MembershipOracle 使用“前向/逆时针”定义来分配责任。 
    /// E.g.在结点 {5,10,15}的环中,键7的责任是5(节点负责其随后的范围)。
    /// </summary>
    internal sealed partial class ConsistentRingProvider :
        IConsistentRingProvider, ISiloStatusListener, IDisposable
    {
        // 内部,以便单位测试能够进入
        internal SiloAddress MyAddress { get; }
        private IRingRange myRange;

        /// silo 成员名单,由未签名地址散列排序,与环程边界和查找键匹配。
        private readonly List<SiloAddress> membershipRingList = new();
        private readonly ILogger log;
        private bool isRunning;
        private readonly uint myKey;
        private readonly RingRangeListenerManager rangeChangeListeners;
        private readonly ISiloStatusOracle _siloStatusOracle;

        public ConsistentRingProvider(SiloAddress siloAddr, ILoggerFactory loggerFactory, ISiloStatusOracle siloStatusOracle)
        {
            log = loggerFactory.CreateLogger<ConsistentRingProvider>();
            MyAddress = siloAddr;
            _siloStatusOracle = siloStatusOracle;
            myKey = unchecked((uint)MyAddress.GetConsistentHashCode());

            myRange = RangeFactory.CreateFullRange(); // 由我负责整个范围
            rangeChangeListeners = new(myRange);

            // 将我加入成员名单
            AddServer(MyAddress);
            Start();
            siloStatusOracle.SubscribeToSiloStatusEvents(this);
        }

        /// <summary>
        /// 返回 silo ,此 silo 认为是密钥的主要所有者
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public SiloAddress? GetPrimaryTargetSilo(uint key)
        {
            return CalculateTargetSilo(key);
        }

        public IRingRange GetMyRange()
        {
            return myRange; // 无法变换, 所以不需要克隆
        }

        private void Start()
        {
            isRunning = true;
        }

        private void Stop()
        {
            isRunning = false;
        }

        internal void AddServer(SiloAddress silo)
        {
            RingRangeListenerManager.NotificationWorkItem? notification = null;
            lock (membershipRingList)
            {
                if (membershipRingList.Contains(silo)) return; // 我们已经拥有了 silo

                int myOldIndex = membershipRingList.IndexOf(MyAddress);

                if (!(membershipRingList.Count == 0 || myOldIndex != -1))
                    throw new OrleansException(string.Format("{0}: Couldn't find my position in the ring {1}.", MyAddress, Utils.EnumerableToString(membershipRingList)));

                // 在排序顺序中插入新的 silo
                uint hash = unchecked((uint)silo.GetConsistentHashCode());

                // 查找最后 silo 与新小于新小于散数的 silo,并在前一段之后插入后一段(这就是为什么我们这里有+1)。
                // 通知 FindLastIndex 可能返回 -1,如果这是清单上的第一个 silo ,但随后
                // 需要时,"索引"会得到0
                int index = membershipRingList.FindLastIndex(siloAddr => unchecked((uint)siloAddr.GetConsistentHashCode()) < hash) + 1;
                membershipRingList.Insert(index, silo);

                // 与触发处理器有关的... 新节点接管了我的一些责任
                if (index == myOldIndex || // 将新节点插入到我的位置
                    (myOldIndex == 0 && index == membershipRingList.Count - 1)) // 我是第一个节点,新服务器是最后一个节点
                {
                    IRingRange oldRange = myRange;
                    myRange = RangeFactory.CreateRange(hash, myKey);
                    notification = PublishRangeChange(oldRange, myRange, false);
                }

                LogDebugAddedServer(log, new(silo), this);
            }

            if (notification is not null)
            {
                rangeChangeListeners.Dispatch(notification);
            }
        }

        public override string ToString()
        {
            lock (membershipRingList)
            {
                if (membershipRingList.Count == 1)
                    return $"[{membershipRingList[0]:H} -> {RangeFactory.CreateFullRange()}]";

                var sb = new StringBuilder().Append('[');
                for (int i = 0; i < membershipRingList.Count; i++)
                {
                    SiloAddress curr = membershipRingList[i];
                    SiloAddress next = membershipRingList[(i + 1) % membershipRingList.Count];
                    IRingRange range = RangeFactory.CreateRange(unchecked((uint)curr.GetConsistentHashCode()), unchecked((uint)next.GetConsistentHashCode()));
                    sb.Append($"{curr:H} -> {range},  ");
                }

                return sb.Append(']').ToString();
            }
        }

        internal void RemoveServer(SiloAddress silo)
        {
            RingRangeListenerManager.NotificationWorkItem? notification = null;
            lock (membershipRingList)
            {
                int indexOfFailedSilo = membershipRingList.IndexOf(silo);
                if (indexOfFailedSilo < 0) return; // 我们已经删除了此 silo

                membershipRingList.RemoveAt(indexOfFailedSilo);

                // 与触发处理器相关
                int myNewIndex = membershipRingList.IndexOf(MyAddress);

                if (myNewIndex == -1)
                    throw new OrleansException($"{MyAddress}: Couldn't find my position in the ring {this}.");

                bool wasMyPred = ((myNewIndex == indexOfFailedSilo) || (myNewIndex == 0 && indexOfFailedSilo == membershipRingList.Count)); // 不需要 ' - 1'
                if (wasMyPred) // 失败节点是我们的前任
                {
                    LogDebugFailedServerWasMyPredecessor(log, wasMyPred, this);

                    IRingRange oldRange = myRange;
                    if (membershipRingList.Count == 1) // 我是唯一一个 剩下的
                    {
                        myRange = RangeFactory.CreateFullRange();
                    }
                    else
                    {
                        int myNewPredIndex = myNewIndex == 0 ? membershipRingList.Count - 1 : myNewIndex - 1;
                        uint myPredecessorsHash = unchecked((uint)membershipRingList[myNewPredIndex].GetConsistentHashCode());

                        myRange = RangeFactory.CreateRange(myPredecessorsHash, myKey);
                    }

                    notification = PublishRangeChange(oldRange, myRange, true);
                }

                LogDebugRemovedServer(log, silo, new(silo), this);
            }

            if (notification is not null)
            {
                rangeChangeListeners.Dispatch(notification);
            }
        }

        public bool SubscribeToRangeChangeEvents(IRingRangeListener observer) => rangeChangeListeners.Subscribe(observer);

        public bool UnSubscribeFromRangeChangeEvents(IRingRangeListener observer) => rangeChangeListeners.Unsubscribe(observer);

        private RingRangeListenerManager.NotificationWorkItem PublishRangeChange(IRingRange old, IRingRange now, bool increased)
        {
            LogDebugNotifyLocalRangeSubscribers(log, old, now, increased);
            return rangeChangeListeners.Publish(
                old,
                now,
                increased,
                (listener, exception) => LogWarningErrorNotifyingListener(
                    log,
                    exception,
                    listener.GetType().FullName,
                    increased ? "expansion" : "contraction",
                    old,
                    now));
        }

        public void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status)
        {
            // 此 silo状态已更改
            if (updatedSilo.Equals(MyAddress))
            {
                if (status.IsTerminating())
                {
                    Stop();
                }
            }
            else // 其他一些 silo的状况变化
            {
                if (status.IsTerminating())
                {
                    RemoveServer(updatedSilo);
                }
                else if (status == SiloStatus.Active)      // 与 SiloStatus.Created 或 SiloStatus.Joining 不做任何不做任何事 -- -- 等到它实际生效
                {
                    AddServer(updatedSilo);
                }
            }
        }

        /// <summary>
        /// 查找拥有给定散列值的 silo 。
        /// 此程序将总是返回一个非null silo 地址, 除非 excludeThisSiloIfStopping 参数是真实的,
        /// 这是唯一的 silo 已知,和此 silo 停止了。 停止了。
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="excludeThisSiloIfStopping"></param>
        /// <returns></returns>
        private SiloAddress? CalculateTargetSilo(uint hash, bool excludeThisSiloIfStopping = true)
        {
            SiloAddress? siloAddress = null;

            lock (membershipRingList)
            {
                // excludeMySelf 开始为 TargetSilo 如果我们不运行, excludeThisSIloIfStopping 标记为是真实的。请在“停止”方法中看到注释。
                bool excludeMySelf = excludeThisSiloIfStopping && !isRunning;

                if (membershipRingList.Count == 0)
                {
                    // 如果会员圈空了 我们就是默认的所有人 除非我们停止
                    return excludeMySelf ? null : MyAddress;
                }

                // 使用时钟... membershipOracle.CalculateTargetSilo() 中的当前代码会逆时针.
                // 如果您想要坚持逆时针反时针, 请在 OrleansReminderMemory中更改“ in()” 方法中的责任定义和责任代数
                // 需要执行二进搜索, 但目前只需跳过按其杂列排序的 silos 列表

                for (int index = 0; index < membershipRingList.Count; ++index)
                {
                    var siloAddr = membershipRingList[index];
                    if (IsSiloNextInTheRing(siloAddr, hash, excludeMySelf))
                    {
                        siloAddress = siloAddr;
                        break;
                    }
                }

                if (siloAddress == null)
                {
                    // 如果在跨经中找不到,则首先应返回 silo (我们在一个环上)
                    // 如果您回到反时针政策, 请在 OrleansReminderMemory中更改“ in()” 方法
                    siloAddress = membershipRingList[0]; // v [membershipRingList.Count - 1];反时针政策
                    // 确定不是我们...
                    if (siloAddress.Equals(MyAddress) && excludeMySelf)
                    {
                        // vs [membershipRingList.Count - 2]; 反时针政策
                        siloAddress = membershipRingList.Count > 1 ? membershipRingList[1] : null;
                    }
                }
            }

            LogTraceCalculatedRingPartitionOwner(log, MyAddress, siloAddress, hash, new(siloAddress));
            return siloAddress;
        }

        private bool IsSiloNextInTheRing(SiloAddress siloAddr, uint hash, bool excludeMySelf)
        {
            return unchecked((uint)siloAddr.GetConsistentHashCode()) >= hash && (!siloAddr.Equals(MyAddress) || !excludeMySelf);
        }

        public void Dispose()
        {
            _siloStatusOracle.UnSubscribeFromSiloStatusEvents(this);
        }

        private readonly struct SiloAddressWithHashLogRecord(SiloAddress siloAddress)
        {
            public override string ToString() => siloAddress.ToStringWithHashCode();
        }

        private readonly struct ConsistentHashCodeLogRecord(SiloAddress? siloAddress)
        {
            public override string ToString() => siloAddress?.GetConsistentHashCode().ToString()!;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Added Server {SiloAddress}. Current view: {CurrentView}"
        )]
        private static partial void LogDebugAddedServer(ILogger logger, SiloAddressWithHashLogRecord siloAddress, ConsistentRingProvider currentView);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Failed server was my predecessor? {WasPredecessor}, updated view {CurrentView}"
        )]
        private static partial void LogDebugFailedServerWasMyPredecessor(ILogger logger, bool wasPredecessor, ConsistentRingProvider currentView);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Removed Server {SiloAddress} hash {Hash}. Current view {CurrentView}"
        )]
        private static partial void LogDebugRemovedServer(ILogger logger, SiloAddress siloAddress, SiloAddressWithHashLogRecord hash, ConsistentRingProvider currentView);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "NotifyLocalRangeSubscribers about old {OldRange} new {NewRange} increased? {IsIncreased}"
        )]
        private static partial void LogDebugNotifyLocalRangeSubscribers(ILogger logger, IRingRange oldRange, IRingRange newRange, bool isIncreased);

        [LoggerMessage(
            EventId = (int)ErrorCode.CRP_Local_Subscriber_Exception,
            Level = LogLevel.Warning,
            Message = "Error notifying listener '{ListenerType}' of ring range {AdjustmentKind} from '{OldRange}' to '{NewRange}'."
        )]
        private static partial void LogWarningErrorNotifyingListener(ILogger logger, Exception exception, string? listenerType, string adjustmentKind, IRingRange oldRange, IRingRange newRange);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Silo {SiloAddress} calculated ring partition owner silo {OwnerAddress} for key {Key}: {Key} --> {OwnerHash}"
        )]
        private static partial void LogTraceCalculatedRingPartitionOwner(ILogger logger, SiloAddress siloAddress, SiloAddress? ownerAddress, uint key, ConsistentHashCodeLogRecord ownerHash);
    }
}
