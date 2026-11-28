using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Orleans.Runtime.ConsistentRing
{
    /// <summary>
    /// 我们采用“向后/顺时针”的定义分配环上的责任范围。
    /// 例如，在由节点 {5, 10, 15} 组成的环中，键 7 由节点 10 负责（节点负责其前面的范围）。
    /// 向后/顺时针的方式与许多覆盖网络一致，例如 Chord、Cassandra 等。
    /// 注意：MembershipOracle 采用“向前/逆时针”的定义分配责任范围。
    /// 例如，在节点 {5, 10, 15} 的环中，键 7 由节点 5 负责（节点负责其后面的范围）。
    /// </summary>
    internal sealed partial class ConsistentRingProvider :
        IConsistentRingProvider, ISiloStatusListener, IDisposable
    {
        // 使用 internal，以便单元测试能够访问。
        internal SiloAddress MyAddress { get; }
        private IRingRange myRange;

        /// 按无符号地址哈希值排序的 Silo 成员列表，与环的范围边界和查找键一致。
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

            myRange = RangeFactory.CreateFullRange(); // 当前 Silo 负责整个范围。
            rangeChangeListeners = new(myRange);

            // 将当前 Silo 添加到成员列表。
            AddServer(MyAddress);
            Start();
            siloStatusOracle.SubscribeToSiloStatusEvents(this);
        }

        /// <summary>
        /// 返回当前 Silo 认为是该键主要所有者的 Silo。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public SiloAddress? GetPrimaryTargetSilo(uint key)
        {
            return CalculateTargetSilo(key);
        }

        public IRingRange GetMyRange()
        {
            return myRange; // 它是不可变的，因此无需克隆。
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
                if (membershipRingList.Contains(silo)) return; // 此 Silo 已存在。

                int myOldIndex = membershipRingList.IndexOf(MyAddress);

                if (!(membershipRingList.Count == 0 || myOldIndex != -1))
                    throw new OrleansException(string.Format("{0}: Couldn't find my position in the ring {1}.", MyAddress, Utils.EnumerableToString(membershipRingList)));

                // 按排序顺序插入新 Silo。
                uint hash = unchecked((uint)silo.GetConsistentHashCode());

                // 找到哈希值小于新 Silo 的最后一个 Silo，并在其后插入新 Silo（因此这里需要加 1）。
                // 注意，如果新 Silo 应当位于列表首位，FindLastIndex 可能返回 -1，但此时
                // index 将如预期得到 0。
                int index = membershipRingList.FindLastIndex(siloAddr => unchecked((uint)siloAddr.GetConsistentHashCode()) < hash) + 1;
                membershipRingList.Insert(index, silo);

                // 与触发处理程序相关：新节点接管了当前节点的部分责任范围。
                if (index == myOldIndex || // 新节点插入到了当前节点所在的位置。
                    (myOldIndex == 0 && index == membershipRingList.Count - 1)) // 当前节点是第一个节点，新服务器是最后一个节点。
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
                if (indexOfFailedSilo < 0) return; // 此 Silo 已被移除。

                membershipRingList.RemoveAt(indexOfFailedSilo);

                // 与触发处理程序相关。
                int myNewIndex = membershipRingList.IndexOf(MyAddress);

                if (myNewIndex == -1)
                    throw new OrleansException($"{MyAddress}: Couldn't find my position in the ring {this}.");

                bool wasMyPred = ((myNewIndex == indexOfFailedSilo) || (myNewIndex == 0 && indexOfFailedSilo == membershipRingList.Count)); // 无需减 1。
                if (wasMyPred) // 故障节点原本是当前节点的前驱。
                {
                    LogDebugFailedServerWasMyPredecessor(log, wasMyPred, this);

                    IRingRange oldRange = myRange;
                    if (membershipRingList.Count == 1) // 仅剩当前节点。
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
            // 当前 Silo 的状态发生了变化。
            if (updatedSilo.Equals(MyAddress))
            {
                if (status.IsTerminating())
                {
                    Stop();
                }
            }
            else // 其他 Silo 的状态发生了变化。
            {
                if (status.IsTerminating())
                {
                    RemoveServer(updatedSilo);
                }
                else if (status == SiloStatus.Active)      // 对 SiloStatus.Created 或 SiloStatus.Joining 不作处理，等待其真正进入活动状态。
                {
                    AddServer(updatedSilo);
                }
            }
        }

        /// <summary>
        /// 查找拥有给定哈希值的 Silo。
        /// 此方法始终返回非 null 的 Silo 地址，除非 excludeThisSiloIfStopping 参数为 true，
        /// 当前 Silo 是唯一已知的 Silo，且它正在停止。
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="excludeThisSiloIfStopping"></param>
        /// <returns></returns>
        private SiloAddress? CalculateTargetSilo(uint hash, bool excludeThisSiloIfStopping = true)
        {
            SiloAddress? siloAddress = null;

            lock (membershipRingList)
            {
                // 如果当前 Silo 未运行且 excludeThisSIloIfStopping 标志为 true，则将自身排除在 TargetSilo 候选之外。参见 Stop 方法中的注释。
                bool excludeMySelf = excludeThisSiloIfStopping && !isRunning;

                if (membershipRingList.Count == 0)
                {
                    // 如果成员环为空，默认由当前 Silo 负责，除非它正在停止。
                    return excludeMySelf ? null : MyAddress;
                }

                // 采用顺时针方向；membershipOracle.CalculateTargetSilo() 当前的代码采用逆时针方向。
                // 若要继续采用逆时针方向，需要修改 In() 方法及 OrleansReminderMemory 中责任范围的定义。
                // 应实现二分查找，但目前仅遍历按哈希值排序的 Silo 列表。

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
                    // 若遍历未找到匹配项，则返回第一个 Silo（因为这是一个环）。
                    // 若恢复逆时针策略，则需要修改 OrleansReminderMemory 中的 In() 方法。
                    siloAddress = membershipRingList[0]; // 对比：逆时针策略使用 [membershipRingList.Count - 1];
                    // 确保选中的不是当前 Silo……
                    if (siloAddress.Equals(MyAddress) && excludeMySelf)
                    {
                        // 对比：逆时针策略使用 [membershipRingList.Count - 2];
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
