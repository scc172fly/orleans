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
                            return false; // 不执行延迟注销。
                        else
                            return (TimeCreated <= DateTime.UtcNow - delayparameter);
                    }

                default:
                    throw new OrleansException("unhandled case");
            }
        }

        public GrainAddress TryAddSingleActivation(GrainAddress address, GrainAddress? previousAddress)
        {
            // 如果已有地址且与 previousAddress 不匹配，则无法添加新地址。
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
                // Grain 本应处于单激活实例模式，但现在却有两个激活实例！
                // 最终应以某种方式将此情况交由 Silo 处理，但目前任意选择其中一个值。
                if (Activation.ActivationId.Key.CompareTo(otherActivation.ActivationId.Key) < 0)
                {
                    var activationToDrop = Activation;
                    Activation = otherActivation;
                    TimeCreated = other.TimeCreated;
                    VersionTag = Random.Shared.Next();
                    return activationToDrop;
                }

                // 保留此激活实例，并销毁另一个。
                return otherActivation;
            }

            return null;
        }
    }

    internal sealed partial class LocalGrainDirectoryPartition
    {
        // 是否应该将这里改为 SortedList<> 或 SortedDictionary，以便更好地分块向跟随节点传输完整
        // 分区？还是应保留 Dictionary，以获得 O(1) 查找而非 O(log n) 查找，考虑到
        // 查找次数远多于传输，因此可以定期排序？
        /// <summary>
        /// 包含从 Grain 到其激活实例列表的映射，以及该列表的版本（ETag）计数器。
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
        /// 将分区中的所有条目作为可枚举集合返回。
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
        /// 向目录分区添加新的激活实例。
        /// </summary>
        /// <returns>此目录映射关联的已注册 ActivationAddress 及版本。</returns>
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
                    // 如果已有条目指向无效的 Silo，则将其移除。
                    if (IsDefunctActivation(grainInfo.Activation))
                    {
                        partitionData[address.GrainId] = grainInfo = new GrainInfo();
                    }
                }

                return new(grainInfo.TryAddSingleActivation(address, previousAddress), grainInfo.VersionTag);
            }
        }

        /// <summary>
        /// 从分区移除指定 Grain 的某个激活实例。
        /// </summary>
        /// <param name="grain">Grain 的标识。</param>
        /// <param name="activation">激活实例的 ID。</param>
        /// <param name="cause">移除激活实例的原因。</param>
        internal void RemoveActivation(GrainId grain, ActivationId activation, UnregistrationCause cause = UnregistrationCause.Force)
        {
            var wasRemoved = false;
            lock (lockable)
            {
                if (partitionData.TryGetValue(grain, out var value) && value.RemoveActivation(activation, cause, this.grainDirectoryOptions.Value.LazyDeregistrationDelay, out wasRemoved))
                {
                    // 如果移除了此 Grain 的最后一个激活实例，则移除整个 Grain 的信息。
                    partitionData.Remove(grain);
                }
            }

            LogTraceRemovingActivation(grain, cause, wasRemoved);
        }

        /// <summary>
        /// 从目录移除 Grain（实际上也移除了其全部激活实例）。
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
        /// 返回该 Grain 的激活实例列表版本号。
        /// 如果未找到 Grain，则返回 -1。
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
        /// 将一个分区合并到另一个分区，假定这些分区互不相交。
        /// 此方法供移交管理器在系统视图（存活 Silo 的集合）发生变化时更新分区。
        /// </summary>
        /// <param name="other"></param>
        /// <returns>必须停用的激活实例。</returns>
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
        /// 遍历分区中的所有条目，并根据给定标志，将
        /// 满足给定谓词的条目移动或复制到新分区中。
        /// 此方法供移交管理器在系统视图（存活 Silo 的集合）发生变化时更新分区。
        /// </summary>
        /// <param name="predicate">筛选谓词（通常判断指定 Grain 是否由某个特定 Silo 拥有）。</param>
        /// <returns>满足给定谓词的条目。</returns>
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
        /// 将内部的分区字典设置为输入参数指定的字典。
        /// 此方法供移交管理器用新分区更新旧分区。
        /// </summary>
        /// <param name="newPartitionData">新的内部分区字典。</param>
        internal void Set(Dictionary<GrainId, GrainInfo> newPartitionData)
        {
            partitionData = newPartitionData;
        }

        /// <summary>
        /// 使用新的增量变更更新分区。
        /// 此方法供移交管理器使用一组增量变更来更新分区。
        /// </summary>
        /// <param name="newPartitionDelta">包含此分区的一组增量更新的字典。
        /// 如果增量中给定键的值有效，则替换分区中的现有条目；
        /// 否则，即当该值为 null 时，移除相应条目。
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
