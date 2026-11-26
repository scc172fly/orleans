using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;

namespace Orleans.Runtime.GrainDirectory
{
    [Serializable]
    internal sealed class GrainInfo
    {
        public const int NO_ETAG = -1;

        public GrainAddress? Activation { get; private set; }

        public DateTime TimeCreated { get; private set; }

        public int VersionTag { get; private set; }

        public bool OkToRemove(UnregistrationCause cause, TimeSpan lazyDeregistrationDelay)
        {
            switch (cause)
            {
                case UnregistrationCause.Force:
                    return true;

                case UnregistrationCause.NonexistentActivation:
                    {
                        var delayparameter = lazyDeregistrationDelay;
                        if (delayparameter <= TimeSpan.Zero)
                            return false; // 没有任何懒惰的注销登记
                        else
                            return (TimeCreated <= DateTime.UtcNow - delayparameter);
                    }

                default:
                    throw new OrleansException("unhandled case");
            }
        }

        public GrainAddress TryAddSingleActivation(GrainAddress address, GrainAddress? previousAddress)
        {
            // 如果现有地址与 "previousAddress" 不符,那么我们不能添加新地址。
            if (Activation is { } existing && (previousAddress is null || !previousAddress.Equals(existing)))
            {
                return existing;
            }
            else
            {
                Activation = address;
                TimeCreated = DateTime.UtcNow;
                VersionTag = Random.Shared.Next();
                return address;
            }
        }

        public bool RemoveActivation(ActivationId act, UnregistrationCause cause, TimeSpan lazyDeregistrationDelay, out bool wasRemoved)
        {
            wasRemoved = false;
            if (Activation is { } existing && existing.ActivationId.Equals(act) && OkToRemove(cause, lazyDeregistrationDelay))
            {
                wasRemoved = true;
                Activation = null;
                VersionTag = Random.Shared.Next();
            }

            return wasRemoved;
        }

        public GrainAddress? Merge(GrainInfo other)
        {
            var otherActivation = other.Activation;
            if (otherActivation is not null && Activation is null)
            {
                Activation = other.Activation;
                TimeCreated = other.TimeCreated;
                VersionTag = Random.Shared.Next();
            }
            else if (Activation is not null && otherActivation is not null)
            {
                // Grain 应该是在单一启动模式, 但我们有两个激活!
                // 最终我们应该把处理此事的委托给 silo, 但是现在,我们任意选择一个价值。
                if (Activation.ActivationId.Key.CompareTo(otherActivation.ActivationId.Key) < 0)
                {
                    var activationToDrop = Activation;
                    Activation = otherActivation;
                    TimeCreated = other.TimeCreated;
                    VersionTag = Random.Shared.Next();
                    return activationToDrop;
                }

                // 保持这个激活并摧毁另一个
                return otherActivation;
            }

            return null;
        }
    }

    internal sealed partial class LocalGrainDirectoryPartition
    {
        // 我们应当将这一修改为 SortedList或 SortedDictionary 以便我们为全船运输 提取更多块
        // 相对于跟随者分区的分区, 或者我们把它留作词典, 以获得 O(1) 而不是 O(log n) 的查看, 假设我们做了
        // 还要多看一眼,所以可以定期整理?
        /// <summary>
        /// 列表的版本(etag) 计数器包含从 grain 到其启动活动的清单的地图以及该列表的版本(etag) 计数器
        /// </summary>
        private Dictionary<GrainId, GrainInfo> partitionData;
        private readonly object lockable;
        private readonly ILogger log;
        private readonly IClusterMembershipService clusterMembershipService;
        private readonly IOptions<GrainDirectoryOptions> grainDirectoryOptions;

        internal int Count { get { return partitionData.Count; } }

        public LocalGrainDirectoryPartition(IClusterMembershipService clusterMembershipService, IOptions<GrainDirectoryOptions> grainDirectoryOptions, ILoggerFactory loggerFactory)
        {
            partitionData = new Dictionary<GrainId, GrainInfo>();
            lockable = new object();
            log = loggerFactory.CreateLogger<LocalGrainDirectoryPartition>();
            this.clusterMembershipService = clusterMembershipService;
            this.grainDirectoryOptions = grainDirectoryOptions;
        }

        private bool IsDefunctActivation(GrainAddress? address)
            => address is null || LocalGrainDirectory.IsDefunctActivation(address, clusterMembershipService.CurrentSnapshot);

        internal void Clear()
        {
            lock (lockable)
            {
                partitionData.Clear();
            }
        }

        /// <summary>
        /// 返回分区中存储的所有条目,作为可数收藏
        /// </summary>
        /// <returns></returns>
        public List<KeyValuePair<GrainId, GrainInfo>> GetItems()
        {
            lock (lockable)
            {
                return partitionData.ToList();
            }
        }

        /// <summary>
        /// 向目录分区添加新激活
        /// </summary>
        /// <returns>登记 ActivationAddress 和与此目录映射相关的版本</returns>
        internal AddressAndTag AddSingleActivation(GrainAddress address, GrainAddress? previousAddress)
        {
            LogTraceAddingSingleActivation(address.SiloAddress, address.GrainId, address.ActivationId);

            if (IsDefunctActivation(address))
            {
                var siloStatus = address.SiloAddress is { } siloAddress
                    ? this.clusterMembershipService.CurrentSnapshot.GetSiloStatus(siloAddress, address.MembershipVersion)
                    : SiloStatus.None;
                throw new OrleansException($"Trying to register {address.GrainId} on invalid silo: {address.SiloAddress}. Known status: {siloStatus}");
            }

            lock (lockable)
            {
                if (!partitionData.TryGetValue(address.GrainId, out var grainInfo))
                {
                    partitionData[address.GrainId] = grainInfo = new GrainInfo();
                }
                else
                {
                    // 如果现有条目指向无效的 silo ,则删除 
                    if (IsDefunctActivation(grainInfo.Activation))
                    {
                        partitionData[address.GrainId] = grainInfo = new GrainInfo();
                    }
                }

                return new(grainInfo.TryAddSingleActivation(address, previousAddress), grainInfo.VersionTag);
            }
        }

        /// <summary>
        /// 从分区中删除给定 grain 的激活
        /// </summary>
        /// <param name="grain">身份的识别、 grain</param>
        /// <param name="activation">激活的id</param>
        /// <param name="cause">删除激活的理由</param>
        internal void RemoveActivation(GrainId grain, ActivationId activation, UnregistrationCause cause = UnregistrationCause.Force)
        {
            var wasRemoved = false;
            lock (lockable)
            {
                if (partitionData.TryGetValue(grain, out var value) && value.RemoveActivation(activation, cause, this.grainDirectoryOptions.Value.LazyDeregistrationDelay, out wasRemoved))
                {
                    // 如果删除了 grain 的最后一次激活, 我们删除整个 grain 信息 
                    partitionData.Remove(grain);
                }
            }

            LogTraceRemovingActivation(grain, cause, wasRemoved);
        }

        /// <summary>
        /// 从目录中删除 grain (以及有效的所有激活)
        /// </summary>
        /// <param name="grain"></param>
        internal void RemoveGrain(GrainId grain)
        {
            lock (lockable)
            {
                partitionData.Remove(grain);
            }
            LogTraceRemovingGrain(grain);
        }

        internal AddressAndTag LookUpActivation(GrainId grain)
        {
            AddressAndTag result;
            lock (lockable)
            {
                if (!partitionData.TryGetValue(grain, out var grainInfo) || grainInfo.Activation is null)
                {
                    return default;
                }

                result = new(grainInfo.Activation, grainInfo.VersionTag);
            }

            if (IsDefunctActivation(result.Address))
            {
                result = new(null, result.VersionTag);
            }

            return result;
        }

        /// <summary>
        /// 返回 grain激活列表的版本号。
        /// 如果找不到 grain , -1 将被返回。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        internal int GetGrainETag(GrainId grain)
        {
            lock (lockable)
            {
                return partitionData.TryGetValue(grain, out var info) ? info.VersionTag : GrainInfo.NO_ETAG;
            }
        }

        /// <summary>
        /// 假设分割区断裂,将一个分割区合并为另一个分割区。
        /// 当系统视图( 即时 silos集) 更改时, 手动管理器应该使用此方法更新分区 。
        /// </summary>
        /// <param name="other"></param>
        /// <returns>活动必须停止</returns>
        internal Dictionary<SiloAddress, List<GrainAddress>>? Merge(LocalGrainDirectoryPartition other)
        {
            Dictionary<SiloAddress, List<GrainAddress>>? activationsToRemove = null;
            lock (lockable)
            {
                foreach (var pair in other.partitionData)
                {
                    ref var grainInfo = ref CollectionsMarshal.GetValueRefOrAddDefault(partitionData, pair.Key, out _);
                    if (grainInfo is { } existing)
                    {
                        LogDebugMergingDisjointPartitions(pair.Key);

                        var activationToDrop = existing.Merge(pair.Value);
                        if (activationToDrop == null) continue;
                        (CollectionsMarshal.GetValueRefOrAddDefault(activationsToRemove ??= new(), activationToDrop.SiloAddress!, out _) ??= new()).Add(activationToDrop);
                    }
                    else
                    {
                        grainInfo = pair.Value;
                    }
                }
            }

            return activationsToRemove;
        }

        /// <summary>
        /// 运行分区和移动/复制(取决于给定的旗帜)中的所有条目
        /// 符合给定的上游线的条目进入新分区。
        /// 当系统视图( 即时 silos集) 更改时, 手动管理器应该使用此方法更新分区 。
        /// </summary>
        /// <param name="predicate">过滤器上游(通常是如果给定 grain 为特定 silo所有)</param>
        /// <returns>符合给定的上游</returns>
        internal List<GrainAddress> Split(Predicate<GrainId> predicate)
        {
            var result = new List<GrainAddress>();

            lock (lockable)
            {
                foreach (var pair in partitionData)
                {
                    if (pair.Value.Activation is { } address && predicate(pair.Key))
                    {
                        result.Add(address);
                    }
                }
            }

            for (var i = result.Count - 1; i >= 0; i--)
            {
                if (IsDefunctActivation(result[i]))
                {
                    result.RemoveAt(i);
                }
            }

            return result;
        }

        /// <summary>
        /// 将内部分区字典设置为输入参数。
        /// 手动管理器应该使用这种方法更新旧分区, 并使用新的分区 。
        /// </summary>
        /// <param name="newPartitionData">新的内部分区字典</param>
        internal void Set(Dictionary<GrainId, GrainInfo> newPartitionData)
        {
            partitionData = newPartitionData;
        }

        /// <summary>
        /// 以新的三角形更改更新分区。
        /// 手动管理器应该使用这种方法更新分区, 并进行一系列三角形更改 。
        /// </summary>
        /// <param name="newPartitionDelta">字典中包含一组三角洲更新到此分区的 。
        /// 如果三角形中给定密钥的值有效,则替换分区中的现有条目。
        /// 否则, i.e.如果数值为无效,则删除相应的条目。
        /// </param>
        internal void Update(Dictionary<GrainId, GrainInfo> newPartitionDelta)
        {
            lock (lockable)
            {
                foreach (var kv in newPartitionDelta)
                {
                    if (kv.Value != null)
                    {
                        partitionData[kv.Key] = kv.Value;
                    }
                    else
                    {
                        partitionData.Remove(kv.Key);
                    }
                }
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            lock (lockable)
            {
                foreach (var grainEntry in partitionData)
                {
                    if (grainEntry.Value.Activation is { } activation)
                    {
                        sb.Append("    ").Append(grainEntry.Key.ToString()).Append("[" + grainEntry.Value.VersionTag + "]").
                            Append(" => ").Append(activation.GrainId.ToString()).
                            Append(" @ ").AppendLine(activation.ToString());
                    }
                }
            }

            return sb.ToString();
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Adding single activation for grain {SiloAddress} {GrainId} {ActivationId}"
        )]
        private partial void LogTraceAddingSingleActivation(SiloAddress? siloAddress, GrainId grainId, ActivationId activationId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Removing activation for grain {GrainId} cause={Cause} was_removed={WasRemoved}"
        )]
        private partial void LogTraceRemovingActivation(GrainId grainId, UnregistrationCause cause, bool wasRemoved);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Removing grain {GrainId}"
        )]
        private partial void LogTraceRemovingGrain(GrainId grainId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "While merging two disjoint partitions, same grain {GrainId} was found in both partitions"
        )]
        private partial void LogDebugMergingDisjointPartitions(GrainId grainId);
    }
}
