using System;
using System.Collections.Generic;
using System.Linq;
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
    internal sealed partial class VirtualBucketsRingProvider :
        IConsistentRingProvider, ISiloStatusListener, IDisposable
    {
        private readonly SortedDictionary<uint, SiloAddress> bucketsMap = new();
        private (uint Hash, SiloAddress SiloAddress)[] sortedBucketsList = null!; // 展平并排序的桶列表，以便 CalculateTargetSilo 快速进行无锁计算。
        private readonly ILogger logger;
        private readonly SiloAddress myAddress;
        private readonly int numBucketsPerSilo;
        private readonly ISiloStatusOracle _siloStatusOracle;
        private readonly RingRangeListenerManager rangeChangeListeners;
        private bool running;
        private IRingRange myRange;

        internal VirtualBucketsRingProvider(SiloAddress siloAddress, ILoggerFactory loggerFactory, int numVirtualBuckets, ISiloStatusOracle siloStatusOracle, ConsistentRingInstruments consistentRingInstruments)
        {
            numBucketsPerSilo = numVirtualBuckets;
            _siloStatusOracle = siloStatusOracle;
            if (numBucketsPerSilo <= 0)
                throw new IndexOutOfRangeException($"numBucketsPerSilo is out of the range. numBucketsPerSilo = {numBucketsPerSilo}");

            logger = loggerFactory.CreateLogger<VirtualBucketsRingProvider>();

            myAddress = siloAddress;
            running = true;
            myRange = RangeFactory.CreateFullRange();
            rangeChangeListeners = new(myRange);

            LogDebugStarting(logger, nameof(VirtualBucketsRingProvider), new(siloAddress));

            consistentRingInstruments.RegisterRingSizeObserve(() => GetRingSize());
            consistentRingInstruments.RegisterMyRangeRingPercentageObserve(() => (float)((IRingRangeInternal)myRange).RangePercentage());
            consistentRingInstruments.RegisterAverageRingPercentageObserve(() =>
            {
                int size = GetRingSize();
                return size == 0 ? 0 : ((float)100.0 / size);
            });

            // 将当前 Silo 添加到成员列表。
            AddServer(myAddress);
            siloStatusOracle.SubscribeToSiloStatusEvents(this);
        }

        private void Stop()
        {
            running = false;
        }

        public IRingRange GetMyRange()
        {
            return myRange;
        }

        private int GetRingSize()
        {
            lock (bucketsMap)
            {
                return bucketsMap.Values.Distinct().Count();
            }
        }

        public bool SubscribeToRangeChangeEvents(IRingRangeListener observer) => rangeChangeListeners.Subscribe(observer);

        public bool UnSubscribeFromRangeChangeEvents(IRingRangeListener observer) => rangeChangeListeners.Unsubscribe(observer);

        private RingRangeListenerManager.NotificationWorkItem PublishRangeChange(IRingRange old, IRingRange now, bool increased)
        {
            LogTraceNotifyLocalRangeSubscribers(logger, old, now, increased);
            return rangeChangeListeners.Publish(
                old,
                now,
                increased,
                (listener, exception) => LogWarningErrorNotifyingListener(
                    logger,
                    exception,
                    listener.GetType().FullName,
                    increased ? "expansion" : "contraction",
                    old,
                    now));
        }

        private void AddServer(SiloAddress silo)
        {
            var hashes = silo.GetUniformHashCodes(numBucketsPerSilo);
            RingRangeListenerManager.NotificationWorkItem notification;
            lock (bucketsMap)
            {
                foreach (var hash in hashes)
                {
                    if (bucketsMap.TryGetValue(hash, out var other))
                    {
                        // 如果两个 Silo 发生冲突，取两者中较小的一个（通常是较早的 Silo，即 epoch 较小者）。
                        if (silo.CompareTo(other) > 0) continue;
                    }
                    bucketsMap[hash] = silo;
                }

                var myOldRange = myRange;
                var myNewRange = UpdateRange();
                LogTraceAddedServer(logger, new(silo), this);

                notification = PublishRangeChange(myOldRange, myNewRange, true);
            }

            rangeChangeListeners.Dispatch(notification);
        }

        private void RemoveServer(SiloAddress silo)
        {
            RingRangeListenerManager.NotificationWorkItem notification;
            lock (bucketsMap)
            {
                if (!bucketsMap.ContainsValue(silo)) return; // 此 Silo 已被移除。

                var hashes = silo.GetUniformHashCodes(numBucketsPerSilo);
                foreach (var hash in hashes)
                {
                    if (bucketsMap.Remove(hash, out var removedSilo) && !removedSilo.Equals(silo))
                    {
                        // 由于哈希冲突非常罕见，先移除桶，再在发现 Silo 不同时补回该桶，
                        // 比每次执行两次查找更合适（第一次检查 Silo 是否相等，
                        // 第二次移除桶）。
                        bucketsMap[hash] = removedSilo;
                    }
                }

                var myOldRange = this.myRange;
                var myNewRange = UpdateRange();

                LogTraceRemovedServer(logger, new(silo), this);

                notification = PublishRangeChange(myOldRange, myNewRange, true);
            }

            rangeChangeListeners.Dispatch(notification);
        }

        private IRingRange UpdateRange()
        {
            var bucketsList = new (uint, SiloAddress)[bucketsMap.Count];
            var idx = 0;
            foreach (var pair in bucketsMap) bucketsList[idx++] = (pair.Key, pair.Value);
            var myNewRange = CalculateRange(bucketsList, myAddress);

            // 捕获当前责任范围及 sortedBucketsList，供之后无锁访问。
            myRange = myNewRange;
            sortedBucketsList = bucketsList;
            return myNewRange;
        }

        private static IRingRange CalculateRange((uint Hash, SiloAddress SiloAddress)[] list, SiloAddress silo)
        {
            var ranges = new List<IRingRange>();
            for (int i = 0; i < list.Length; i++)
            {
                var curr = list[i];
                var next = list[(i + 1) % list.Length];
                // 采用“向后/顺时针”的定义分配环上的责任范围。
                if (next.SiloAddress.Equals(silo))
                {
                    IRingRange range = RangeFactory.CreateRange(curr.Hash, next.Hash);
                    ranges.Add(range);
                }
            }
            return RangeFactory.CreateRange(ranges);
        }

        // 用于调试和日志记录。
        public override string ToString()
        {
            var sortedList = GetRanges();
            sortedList.Sort((t1, t2) => t1.Value.RangePercentage().CompareTo(t2.Value.RangePercentage()));
            return Utils.EnumerableToString(sortedList, kv => $"{kv.Key} -> {kv.Value}");
        }

        // internal：仅供测试和日志记录使用！
        internal List<(SiloAddress Key, IRingRangeInternal Value)> GetRanges()
        {
            List<SiloAddress> silos;
            (uint, SiloAddress)[] snapshotBucketsList;
            lock (bucketsMap)
            {
                silos = bucketsMap.Values.Distinct().ToList();
                snapshotBucketsList = sortedBucketsList;
            }
            var ranges = new List<(SiloAddress, IRingRangeInternal)>(silos.Count);
            foreach (var silo in silos)
            {
                var range = (IRingRangeInternal)CalculateRange(snapshotBucketsList, silo);
                ranges.Add((silo, range));
            }

            return ranges;
        }

        public void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status)
        {
            // 当前 Silo 的状态发生了变化。
            if (updatedSilo.Equals(myAddress))
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

        public SiloAddress? GetPrimaryTargetSilo(uint key)
        {
            return CalculateTargetSilo(key);
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
            // 保存一个指向 sortedBucketsList 的私有引用，
            // 这样即使其他代码正在更改 sortedBucketsList 引用，也不会在当前操作进行到一半时改变我们使用的引用。
            // 这是编写无锁代码的技巧！
            var snapshotBucketsList = sortedBucketsList;

            // 如果当前 Silo 未运行且 excludeThisSIloIfStopping 标志为 true，则将自身排除在 TargetSilo 候选之外。参见 Stop 方法中的注释。
            bool excludeMySelf = excludeThisSiloIfStopping && !running;

            if (snapshotBucketsList.Length == 0)
            {
                // 如果成员环为空，默认由当前 Silo 负责，除非它正在停止。
                return excludeMySelf ? null : myAddress;
            }

            // 采用顺时针方向；membershipOracle.CalculateTargetSilo() 当前的代码采用逆时针方向。
            // 若要继续采用逆时针方向，需要修改 In() 方法及 OrleansReminderMemory 中责任范围的定义。
            // 应实现二分查找，但目前仅遍历按哈希值排序的 Silo 列表。
            (uint Hash, SiloAddress? SiloAddress) s = default;
            foreach (var tuple in snapshotBucketsList)
            {
                if (tuple.Hash >= hash && // 逆时针责任范围使用 <= hash。
                    (!tuple.SiloAddress.Equals(myAddress) || !excludeMySelf))
                {
                    s = tuple;
                    break;
                }
            }

            if (s.SiloAddress == null)
            {
                // 若遍历未找到匹配项，则返回第一个 Silo（因为这是一个环）。
                // 若恢复逆时针策略，则需要修改 OrleansReminderMemory 中的 In() 方法。
                s = snapshotBucketsList[0]; // 对比：逆时针策略使用 [membershipRingList.Count - 1];
                // 确保选中的不是当前 Silo……
                if (s.SiloAddress!.Equals(myAddress) && excludeMySelf)
                {
                    // 对比：逆时针策略使用 [membershipRingList.Count - 2];
                    s = snapshotBucketsList.Length > 1 ? snapshotBucketsList[1] : default;
                }
            }

            LogTraceCalculatedRingPartitionOwner(logger, s.SiloAddress, hash, s.Hash);

            return s.SiloAddress;
        }

        public void Dispose()
        {
            _siloStatusOracle.UnSubscribeFromSiloStatusEvents(this);
        }

        private readonly struct SiloAddressLogValue
        {
            private readonly SiloAddress _siloAddress;

            public SiloAddressLogValue(SiloAddress siloAddress)
            {
                _siloAddress = siloAddress;
            }

            public override string ToString() => _siloAddress.ToStringWithHashCode();
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Starting {Name} on silo {SiloAddress}."
        )]
        private static partial void LogDebugStarting(ILogger logger, string name, SiloAddressLogValue siloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.CRP_Notify,
            Level = LogLevel.Trace,
            Message = "NotifyLocalRangeSubscribers about old {Old} new {New} increased? {IsIncrease}"
        )]
        private static partial void LogTraceNotifyLocalRangeSubscribers(ILogger logger, IRingRange old, IRingRange @new, bool isIncrease);

        [LoggerMessage(
            EventId = (int)ErrorCode.CRP_Local_Subscriber_Exception,
            Level = LogLevel.Warning,
            Message = "Error notifying listener '{ListenerType}' of ring range {AdjustmentKind} from '{OldRange}' to '{NewRange}'."
        )]
        private static partial void LogWarningErrorNotifyingListener(ILogger logger, Exception exception, string? listenerType, string adjustmentKind, IRingRange oldRange, IRingRange newRange);

        [LoggerMessage(
            EventId = (int)ErrorCode.CRP_Added_Silo,
            Level = LogLevel.Trace,
            Message = "Added Server {SiloAddress}. Current view: {CurrentView}"
        )]
        private static partial void LogTraceAddedServer(ILogger logger, SiloAddressLogValue siloAddress, VirtualBucketsRingProvider currentView);

        [LoggerMessage(
            EventId = (int)ErrorCode.CRP_Removed_Silo,
            Level = LogLevel.Trace,
            Message = "Removed Server {SiloAddress}. Current view: {CurrentView}"
        )]
        private static partial void LogTraceRemovedServer(ILogger logger, SiloAddressLogValue siloAddress, VirtualBucketsRingProvider currentView);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Calculated ring partition owner silo {Owner} for key {Key}: {Key} --> {OwnerHash}"
        )]
        private static partial void LogTraceCalculatedRingPartitionOwner(ILogger logger, SiloAddress? owner, uint key, uint ownerHash);
    }
}
