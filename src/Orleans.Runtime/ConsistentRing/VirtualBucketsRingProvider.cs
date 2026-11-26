using System;
using System.Collections.Generic;
using System.Linq;
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
    internal sealed partial class VirtualBucketsRingProvider :
        IConsistentRingProvider, ISiloStatusListener, IDisposable
    {
        private readonly SortedDictionary<uint, SiloAddress> bucketsMap = new();
        private (uint Hash, SiloAddress SiloAddress)[] sortedBucketsList = null!; // 用于快速无锁计算 CalculateTargetSilo的已平定分类桶列表
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

            // 将我加入成员名单
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
                        // 如果两次 silos 冲突,取两者中较轻者(通常是较老者,即较低时代)
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
                if (!bucketsMap.ContainsValue(silo)) return; // 我们已经删除了此 silo

                var hashes = silo.GetUniformHashCodes(numBucketsPerSilo);
                foreach (var hash in hashes)
                {
                    if (bucketsMap.Remove(hash, out var removedSilo) && !removedSilo.Equals(silo))
                    {
                        // 散状碰撞非常罕见 所以最好先把桶子拿掉 再追溯
                        // 如果 silos 不同,而不是做2次调查(1次检查是否 silos
                        // 等值, 然后第二秒再除去桶)
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

            // 捕捉我的区域, sortedBucketsList 以后可以进入无锁通道
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
                // 背向/时针”的定义,以分配环上的责任。
                if (next.SiloAddress.Equals(silo))
                {
                    IRingRange range = RangeFactory.CreateRange(curr.Hash, next.Hash);
                    ranges.Add(range);
                }
            }
            return RangeFactory.CreateRange(ranges);
        }

        // 用于调试/博客
        public override string ToString()
        {
            var sortedList = GetRanges();
            sortedList.Sort((t1, t2) => t1.Value.RangePercentage().CompareTo(t2.Value.RangePercentage()));
            return Utils.EnumerableToString(sortedList, kv => $"{kv.Key} -> {kv.Value}");
        }

        // 内部: 只用于测试/ 博客!
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
            // 此 silo状态已更改
            if (updatedSilo.Equals(myAddress))
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

        public SiloAddress? GetPrimaryTargetSilo(uint key)
        {
            return CalculateTargetSilo(key);
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
            // 将私有引用点置于 sortedBucketsList,
            // 所以如果有人在更改 sortedBucketsList 参考, 我们不会在行动过程中改变它。
            // 写无锁密码的把戏!
            var snapshotBucketsList = sortedBucketsList;

            // excludeMySelf 开始为 TargetSilo 如果我们不运行, excludeThisSIloIfStopping 标记为是真实的。请在“停止”方法中看到注释。
            bool excludeMySelf = excludeThisSiloIfStopping && !running;

            if (snapshotBucketsList.Length == 0)
            {
                // 如果会员圈空了 我们就是默认的所有人 除非我们停止
                return excludeMySelf ? null : myAddress;
            }

            // 使用时钟... membershipOracle.CalculateTargetSilo() 中的当前代码会逆时针.
            // 如果您想要坚持逆时针反时针, 请在 OrleansReminderMemory中更改“ in()” 方法中的责任定义和责任代数
            // 需要执行二进搜索, 但目前只需跳过按其杂列排序的 silos 列表
            (uint Hash, SiloAddress? SiloAddress) s = default;
            foreach (var tuple in snapshotBucketsList)
            {
                if (tuple.Hash >= hash && // 用于反时针责任的hash
                    (!tuple.SiloAddress.Equals(myAddress) || !excludeMySelf))
                {
                    s = tuple;
                    break;
                }
            }

            if (s.SiloAddress == null)
            {
                // 如果在跨经中找不到,则首先应返回 silo (我们在一个环上)
                // 如果您回到反时针政策, 请在 OrleansReminderMemory中更改“ in()” 方法
                s = snapshotBucketsList[0]; // v [membershipRingList.Count - 1];反时针政策
                // 确定不是我们...
                if (s.SiloAddress!.Equals(myAddress) && excludeMySelf)
                {
                    // vs [membershipRingList.Count - 2]; 反时针政策
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
