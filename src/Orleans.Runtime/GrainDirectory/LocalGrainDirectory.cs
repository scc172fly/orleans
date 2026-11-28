using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;
using Orleans.Internal;
using Orleans.Runtime.Internal;
using Orleans.Runtime.Diagnostics;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.GrainDirectory
{
    internal sealed partial class LocalGrainDirectory : ILocalGrainDirectory, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly ILogger log;
        private readonly SiloAddress? seed;
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly IClusterMembershipService clusterMembershipService;
        private readonly IInternalGrainFactory grainFactory;
        private readonly ActivationDirectory localActivations;
        private readonly IServiceProvider _serviceProvider;
        private readonly DirectoryInstruments _directoryInstruments;
        private readonly CancellationTokenSource _membershipUpdatesCancellation = new();
        private DirectoryMembership directoryMembership = DirectoryMembership.Default;
        private ClusterMembershipSnapshot appliedClusterMembershipSnapshot = ClusterMembershipSnapshot.Default;
        private GrainDirectoryResolver? grainDirectoryResolver;
        private bool hasAppliedClusterMembershipSnapshot;

        // 考虑将这些常量移到合适的位置。
        internal const int HOP_LIMIT = 6; // 远程请求最多转发 5 次。
        public static readonly TimeSpan RETRY_DELAY = TimeSpan.FromMilliseconds(200); // 每次转发之间暂停 200 毫秒，使成员资格目录逐渐稳定。
        internal bool Running;
        private Task? membershipUpdatesTask;

        internal SiloAddress MyAddress { get; }

        internal IGrainDirectoryCache DirectoryCache { get; }
        private readonly bool disposeDirectoryCache;
        internal LocalGrainDirectoryPartition DirectoryPartition { get; }

        public RemoteGrainDirectory RemoteGrainDirectory { get; }
        public RemoteGrainDirectory CacheValidator { get; }
        internal LocalGrainDirectoryClientCompatibility? DistributedGrainDirectoryClientCompatibility { get; }
        internal ImmutableArray<LocalGrainDirectoryPartitionCompatibility> DistributedGrainDirectoryPartitionCompatibilities { get; }

        internal GrainDirectoryHandoffManager HandoffManager { get; }
        internal DirectoryInstruments DirectoryInstruments => _directoryInstruments;

        public LocalGrainDirectory(
            IServiceProvider serviceProvider,
            ILocalSiloDetails siloDetails,
            ISiloStatusOracle siloStatusOracle,
            IClusterMembershipService clusterMembershipService,
            IInternalGrainFactory grainFactory,
            Factory<LocalGrainDirectoryPartition> grainDirectoryPartitionFactory,
            IOptions<DevelopmentClusterMembershipOptions> developmentClusterMembershipOptions,
            IOptions<GrainDirectoryOptions> grainDirectoryOptions,
            ILoggerFactory loggerFactory,
            DirectoryInstruments directoryInstruments,
            SystemTargetShared systemTargetShared)
        {
            this.log = loggerFactory.CreateLogger<LocalGrainDirectory>();
            _directoryInstruments = directoryInstruments;

            MyAddress = siloDetails.SiloAddress;

            this.siloStatusOracle = siloStatusOracle;
            this.clusterMembershipService = clusterMembershipService;
            this.grainFactory = grainFactory;
            this.localActivations = systemTargetShared.ActivationDirectory;

            DirectoryCache = GrainDirectoryCacheFactory.CreateGrainDirectoryCache(serviceProvider, grainDirectoryOptions.Value, out this.disposeDirectoryCache, _directoryInstruments);

            var primarySiloEndPoint = developmentClusterMembershipOptions.Value.PrimarySiloEndpoint;
            if (primarySiloEndPoint != null)
            {
                this.seed = this.MyAddress.Endpoint.Equals(primarySiloEndPoint) ? this.MyAddress : SiloAddress.New(primarySiloEndPoint, 0);
            }

            DirectoryPartition = grainDirectoryPartitionFactory();
            HandoffManager = new GrainDirectoryHandoffManager(this, siloStatusOracle, clusterMembershipService, grainFactory, loggerFactory);

            // 旧版远程端点为默认目录提供服务。命名目录使用各自的端点。
            var distributedDirectoryActive = serviceProvider.GetService<DistributedGrainDirectory>() is not null;
            var distributedDirectoryIsDefault = serviceProvider.GetKeyedService<IGrainDirectory>(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY) is DistributedGrainDirectory;
            RemoteGrainDirectory = new RemoteGrainDirectory(this, Constants.DirectoryServiceType, systemTargetShared, registerAsSystemTarget: !distributedDirectoryIsDefault);
            CacheValidator = new RemoteGrainDirectory(this, Constants.DirectoryCacheValidatorType, systemTargetShared, registerAsSystemTarget: !distributedDirectoryIsDefault);
            DistributedGrainDirectoryClientCompatibility = distributedDirectoryActive ? null : new LocalGrainDirectoryClientCompatibility(systemTargetShared);
            if (!distributedDirectoryActive)
            {
                var partitionsPerSilo = grainDirectoryOptions.Value.PartitionsPerSilo;
                ArgumentOutOfRangeException.ThrowIfLessThan(partitionsPerSilo, 1, nameof(GrainDirectoryOptions.PartitionsPerSilo));
                var compatibilityPartitions = ImmutableArray.CreateBuilder<LocalGrainDirectoryPartitionCompatibility>(partitionsPerSilo);
                for (var partitionIndex = 0; partitionIndex < partitionsPerSilo; partitionIndex++)
                {
                    compatibilityPartitions.Add(new LocalGrainDirectoryPartitionCompatibility(this, systemTargetShared, partitionIndex));
                }

                DistributedGrainDirectoryPartitionCompatibilities = compatibilityPartitions.MoveToImmutable();
            }

            this.directoryMembership = DirectoryMembership.Create([MyAddress]);

            _directoryInstruments.RegisterDirectoryPartitionSizeObserve(() => DirectoryPartition.Count);
            _directoryInstruments.RegisterMyPortionRingDistanceObserve(() => RingDistanceToSuccessor());
            _directoryInstruments.RegisterMyPortionRingPercentageObserve(() => this.RingDistanceToSuccessor() / (float)(int.MaxValue * 2L) * 100);
            _directoryInstruments.RegisterMyPortionAverageRingPercentageObserve(() =>
            {
                var ring = this.directoryMembership.MembershipRingList;
                return ring.Count == 0 ? 0 : (100 / (float)ring.Count);
            });
            _directoryInstruments.RegisterRingSizeObserve(() => this.directoryMembership.MembershipRingList.Count);
            _serviceProvider = serviceProvider;
        }

        public void Start()
        {
            LogDebugStart();

            Running = true;
            using var _ = new ExecutionContextSuppressor();
            membershipUpdatesTask = Task.Run(() => ProcessMembershipUpdates(_membershipUpdatesCancellation.Token));
        }

        // 注意：当触发 Stop 事件时，此实现停止处理目录变更请求（Register、Unregister 等）。
        // 这意味着可能存在一小段时间，没有任何 Silo 认为自己是某组
        // Grain 目录信息的所有者（就更新而言），从而使需要创建新激活实例的应用程序请求超时。
        // 另一种方式是允许 Silo 在移交分区之后继续处理请求，但这样一来，相关变更
        // 会收到成功响应，却不会反映在目录的最终状态中。
        // 如果认为后一种权衡更好，这里很容易修改。
        public async Task StopAsync()
        {
            // 这将使远程写入请求被转发到即将成为新所有者的 Silo。
            // 随着成员资格逐渐稳定，请求可能在节点之间往返转发一段时间，但最终要么由 Grain 的
            // 新所有者处理，要么失败。无论哪种情况，都可避免在此 Silo 已经开始停止之后仍有请求成功，
            // 以免请求成功后，其产生的变更却未能移交给新所有者。

            // 将 Running 标记为 false，会使 CalculateGrainDirectoryPartition(grainId) 排除当前 Silo。
            Running = false;
            _membershipUpdatesCancellation.Cancel();
            try
            {
                if (membershipUpdatesTask is { } task)
                {
                    await task.SuppressThrowing();
                }
            }
            finally
            {
                _membershipUpdatesCancellation.Dispose();
            }

            if (this.disposeDirectoryCache)
            {
                try
                {
                    await GrainDirectoryCacheFactory.DisposeGrainDirectoryCacheAsync(DirectoryCache);
                }
                catch (Exception exception)
                {
                    LogWarningDisposeDirectoryCacheFailed(exception);
                }
            }

            DirectoryPartition.Clear();
            DirectoryCache.Clear();
        }

        private async Task ProcessMembershipUpdates(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ApplyMembershipSnapshot();

                    await foreach (var _ in clusterMembershipService.MembershipUpdates.WithCancellation(cancellationToken))
                    {
                        // 始终应用最新快照。
                        await ApplyMembershipSnapshot();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    LogErrorProcessingMembershipUpdates(exception);

                    try
                    {
                        await clusterMembershipService.Refresh(cancellationToken: cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception refreshException)
                    {
                        LogWarningRefreshingClusterMembershipFailed(refreshException);
                    }

                    await Task.Delay(RETRY_DELAY, cancellationToken).SuppressThrowing();
                }
            }
        }

        private Task ApplyMembershipSnapshot()
        {
            return CacheValidator.RunOrQueueTask(() =>
            {
                ApplyMembershipSnapshotCore();
                return Task.CompletedTask;
            });

            void ApplyMembershipSnapshotCore()
            {
                if (!Running)
                {
                    return;
                }

                var snapshot = clusterMembershipService.CurrentSnapshot;
                var previousSnapshot = hasAppliedClusterMembershipSnapshot ? appliedClusterMembershipSnapshot : ClusterMembershipSnapshot.Default;
                if (hasAppliedClusterMembershipSnapshot && snapshot.Version <= previousSnapshot.Version)
                {
                    return;
                }

                var previousMembership = CreateDirectoryMembership(previousSnapshot);
                var targetMembership = CreateDirectoryMembership(snapshot);
                this.directoryMembership = targetMembership;

                var removedSilos = GetMembershipDifference(previousMembership, targetMembership);
                var addedSilos = GetMembershipDifference(targetMembership, previousMembership);

                ProcessSiloStatusChanges(snapshot, previousSnapshot, previousMembership);
                AdjustLocalDirectory(snapshot);
                AdjustLocalCache(snapshot, targetMembership);

                foreach (var silo in removedSilos)
                {
                    LogDebugSiloRemovedSilo(MyAddress, silo);
                }

                foreach (var silo in addedSilos)
                {
                    HandoffManager.ProcessSiloAddEvent(silo);
                    LogDebugSiloAddedSilo(MyAddress, silo);
                }

                appliedClusterMembershipSnapshot = snapshot;
                hasAppliedClusterMembershipSnapshot = true;
                GrainDirectoryEvents.EmitMembershipVersionApplied(MyAddress, snapshot.Version);
            }
        }

        private void ProcessSiloStatusChanges(
            ClusterMembershipSnapshot snapshot,
            ClusterMembershipSnapshot previousSnapshot,
            DirectoryMembership previousMembership)
        {
            var changes = previousSnapshot.Version != MembershipVersion.MinValue
                ? snapshot.CreateUpdate(previousSnapshot)
                : snapshot.AsUpdate();
            var statusChanges = new List<ClusterMember>();
            foreach (var change in changes.Changes)
            {
                if (!change.SiloAddress.Equals(MyAddress) && change.Status.IsTerminating())
                {
                    statusChanges.Add(change);
                }
            }

            statusChanges.Sort(static (left, right) => CompareSiloAddress(left.SiloAddress, right.SiloAddress));
            foreach (var change in statusChanges)
            {
                OnSiloStatusChange(previousMembership, change.SiloAddress, change.Status);
            }
        }

        private DirectoryMembership CreateDirectoryMembership(ClusterMembershipSnapshot snapshot)
        {
            var members = new List<SiloAddress>();
            foreach (var member in snapshot.Members)
            {
                if (member.Value.Status == SiloStatus.Active)
                {
                    members.Add(member.Key);
                }
            }

            if (Running && !members.Contains(MyAddress))
            {
                members.Add(MyAddress);
            }

            return DirectoryMembership.Create(members);
        }

        private List<SiloAddress> GetMembershipDifference(
            DirectoryMembership currentMembership,
            DirectoryMembership otherMembership)
        {
            var result = new List<SiloAddress>();
            foreach (var silo in currentMembership.MembershipRingList)
            {
                if (!silo.Equals(MyAddress) && !otherMembership.MembershipCache.Contains(silo))
                {
                    result.Add(silo);
                }
            }

            return result;
        }

        private Task RefreshMembershipIfNewer(List<GrainAddress> addresses)
        {
            var targetVersion = MembershipVersion.MinValue;
            foreach (var address in addresses)
            {
                if (address.MembershipVersion > targetVersion)
                {
                    targetVersion = address.MembershipVersion;
                }
            }

            return RefreshMembershipIfNewer(targetVersion);
        }

        private async Task RefreshMembershipIfNewer(MembershipVersion targetVersion)
        {
            if (targetVersion <= appliedClusterMembershipSnapshot.Version)
            {
                return;
            }

            if (targetVersion > clusterMembershipService.CurrentSnapshot.Version)
            {
                await clusterMembershipService.Refresh(targetVersion);
            }

            await ApplyMembershipSnapshot();
        }

        private void OnSiloStatusChange(DirectoryMembership previousMembership, SiloAddress updatedSilo, SiloStatus status)
        {
            if (updatedSilo.Equals(MyAddress) || !status.IsTerminating())
            {
                return;
            }

            var activationsToShutdown = new List<IGrainContext>();
            var resolver = grainDirectoryResolver ??= _serviceProvider.GetRequiredService<GrainDirectoryResolver>();
            foreach (var activation in localActivations)
            {
                try
                {
                    var activationData = activation.Value;
                    var placementStrategy = activationData.GetComponent<PlacementStrategy>();
                    var isUsingGrainDirectory = placementStrategy is { IsUsingGrainDirectory: true };
                    if (!isUsingGrainDirectory || !resolver.IsUsingDefaultDirectory(activationData.GrainId.Type))
                    {
                        continue;
                    }

                    if (!updatedSilo.Equals(CalculateGrainDirectoryPartition(activationData.GrainId, previousMembership)))
                    {
                        continue;
                    }

                    activationsToShutdown.Add(activationData);
                }
                catch (Exception exception)
                {
                    LogErrorSiloStatusChangeNotification(new(updatedSilo), exception);
                }
            }

            if (activationsToShutdown.Count == 0)
            {
                return;
            }

            LogInfoSiloStatusChangeNotification(activationsToShutdown.Count, new(updatedSilo), status);

            var reasonText = $"This activation is being deactivated because server {updatedSilo} entered status {status} and was responsible for this activation's grain directory registration.";
            var reason = new DeactivationReason(DeactivationReasonCode.DirectoryFailure, reasonText);
            foreach (var activation in activationsToShutdown)
            {
                try
                {
                    activation.Deactivate(reason, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    LogErrorDeactivatingActivationForRemovedSilo(exception, activation.GrainId, new(updatedSilo));
                }
            }
        }

        private void AdjustLocalDirectory(ClusterMembershipSnapshot snapshot)
        {
            var activationsToRemove = new List<(GrainId, ActivationId)>();
            foreach (var entry in this.DirectoryPartition.GetItems())
            {
                if (entry.Value.Activation is { } address)
                {
                    if (IsDefunctActivation(address, snapshot))
                    {
                        activationsToRemove.Add((entry.Key, address.ActivationId));
                    }
                }
            }

            foreach (var activation in activationsToRemove)
            {
                DirectoryPartition.RemoveActivation(activation.Item1, activation.Item2);
            }
        }

        private void AdjustLocalCache(ClusterMembershipSnapshot snapshot, DirectoryMembership targetMembership)
        {
            foreach (var tuple in DirectoryCache.KeyValues)
            {
                var activationAddress = tuple.ActivationAddress;

                // 移除当前已由本地拥有的条目。应当从本地目录分区获取这些条目。
                if (MyAddress.Equals(CalculateGrainDirectoryPartition(activationAddress.GrainId, targetMembership)))
                {
                    DirectoryCache.Remove(activationAddress.GrainId);
                    continue;
                }

                if (IsDefunctActivation(activationAddress, snapshot))
                {
                    DirectoryCache.Remove(activationAddress.GrainId);
                }
            }
        }

        internal static bool IsDefunctActivation(GrainAddress address, ClusterMembershipSnapshot snapshot)
        {
            if (address.SiloAddress is not { } silo)
            {
                return true;
            }

            return snapshot.GetSiloStatus(silo, address.MembershipVersion) == SiloStatus.Dead;
        }

        internal SiloAddress? FindPredecessor(SiloAddress silo)
        {
            var existing = directoryMembership.MembershipRingList;
            int index = existing.IndexOf(silo);
            if (index == -1)
            {
                LogWarningFindPredecessorSiloNotInList(silo);
                return null;
            }

            return existing.Count > 1 ? existing[(index == 0 ? existing.Count : index) - 1] : null;
        }

        internal SiloAddress? FindSuccessor(SiloAddress silo)
        {
            var existing = directoryMembership.MembershipRingList;
            int index = existing.IndexOf(silo);
            if (index == -1)
            {
                LogWarningFindSuccessorSiloNotInList(silo);
                return null;
            }

            return existing.Count > 1 ? existing[(index + 1) % existing.Count] : null;
        }

        private bool IsValidSilo(SiloAddress? silo) => siloStatusOracle.IsFunctionalDirectory(silo!);

        /// <summary>
        /// 查找拥有给定 Grain ID 的目录信息的 Silo。
        /// 只有当前 Silo 是集群中唯一的 Silo 且正在关闭时，此方法才会返回 null。
        /// </summary>
        /// <param name="grainId"></param>
        /// <returns></returns>
        public SiloAddress? CalculateGrainDirectoryPartition(GrainId grainId)
            => CalculateGrainDirectoryPartition(grainId, this.directoryMembership);

        private SiloAddress? CalculateGrainDirectoryPartition(GrainId grainId, DirectoryMembership membership)
        {
            // 对特殊 Grain 进行特殊处理。
            if (grainId.IsSystemTarget())
            {
                if (Constants.SystemMembershipTableType.Equals(grainId.Type))
                {
                    if (seed == null)
                    {
                        var errorMsg =
                            $"Development clustering cannot run without a primary silo. " +
                            $"Please configure {nameof(DevelopmentClusterMembershipOptions)}.{nameof(DevelopmentClusterMembershipOptions.PrimarySiloEndpoint)} " +
                            "or provide a primary silo address to the UseDevelopmentClustering extension. " +
                            "Alternatively, you may want to use reliable membership, such as Azure Table.";
                        throw new ArgumentException(errorMsg, "grainId = " + grainId);
                    }
                }

                LogTraceSystemTargetLookup(MyAddress, grainId, MyAddress);

                // 每个 Silo 都拥有自己的系统目标。
                return MyAddress;
            }

            SiloAddress? siloAddress = null;
            int hash = unchecked((int)grainId.GetUniformHashCode());

            // 如果当前 Silo 未运行且 excludeThisSIloIfStopping 标志为 true，则将自身排除在 TargetSilo 候选之外。参见 Stop 方法中的注释。
            // 已移除 excludeThisSIloIfStopping 标志，因为认为该标志使逻辑不必要地复杂化。如果发现它
            // 有实际价值，可以重新添加。
            bool excludeMySelf = !Running;

            var existing = membership;
            if (existing.MembershipRingList.Count == 0)
            {
                // 如果成员环为空，默认由当前 Silo 负责，除非它正在停止。
                return !Running ? null : MyAddress;
            }

            // 应实现二分查找，但目前仅遍历按哈希值排序的 Silo 列表。
            for (var index = existing.MembershipRingList.Count - 1; index >= 0; --index)
            {
                var item = existing.MembershipRingList[index];
                if (IsSiloNextInTheRing(item, hash, excludeMySelf))
                {
                    siloAddress = item;
                    break;
                }
            }

            if (siloAddress == null)
            {
                // 若遍历未找到匹配项，则选择最后一个 Silo（因为这是一个环）。
                // 前面已检查列表不为空，因此此操作始终是安全的。
                siloAddress = existing.MembershipRingList[existing.MembershipRingList.Count - 1];
                // 确保选中的不是当前 Silo……
                if (siloAddress.Equals(MyAddress) && excludeMySelf)
                {
                    siloAddress = existing.MembershipRingList.Count > 1 ? existing.MembershipRingList[existing.MembershipRingList.Count - 2] : null;
                }
            }

            if (log.IsEnabled(LogLevel.Trace))
                LogTraceCalculatedDirectoryPartitionOwner(
                    MyAddress,
                    siloAddress,
                    grainId,
                    hash,
                    siloAddress?.GetConsistentHashCode());
            return siloAddress;
        }

        public SiloAddress? CheckIfShouldForward(GrainId grainId, int hopCount, string operationDescription)
        {
            var owner = CalculateGrainDirectoryPartition(grainId);

            if (owner is null || owner.Equals(MyAddress))
            {
                // 要么当前 Silo 不知道其他 Silo 且正在停止，要么当前 Silo 就是所有者。
                // null 表示应在本地执行操作。
                // 如果当前主机正在终止，则注册到此主机的所有 Grain 都必须终止。
                return null;
            }

            if (hopCount >= HOP_LIMIT)
            {
                // 由于转发跳数已经过多，不再转发。
                throw new OrleansException($"Silo {MyAddress} is not owner of {grainId}, cannot forward {operationDescription} to owner {owner} because hop limit is reached");
            }

            // 转发到当前认为是所有者的 Silo。
            return owner;
        }

        public Task<AddressAndTag> RegisterAsync(GrainAddress address, int hopCount) => RegisterAsync(address, previousAddress: null, hopCount: hopCount);

        public async Task<AddressAndTag> RegisterAsync(GrainAddress address, GrainAddress? previousAddress, int hopCount)
        {
            if (hopCount > 0)
            {
                _directoryInstruments.RegistrationsSingleActRemoteReceived.Add(1);
            }
            else
            {
                _directoryInstruments.RegistrationsSingleActIssued.Add(1);
            }

            await RefreshMembershipIfNewer(address.MembershipVersion);

            // 检查目录分区的所有者是否为其他 Silo（如果当前 Silo 是所有者，则返回 null）。
            var forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, "RegisterAsync");

            // 如果目录分区所有权再次发生变化，首次接收转发请求的 Silo（hopCount == 1）会立即继续转发。
            // 请求已经被再次转发后，先暂停并重新检查，再决定是否继续转发。
            if (hopCount > 1 && forwardAddress != null)
            {
                await Task.Delay(RETRY_DELAY);
                forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, "RegisterAsync");
                if (forwardAddress is not null)
                {
                    int hash = unchecked((int)address.GrainId.GetUniformHashCode());
                    LogWarningRegisterAsyncNotOwner(
                        address,
                        hash,
                        forwardAddress,
                        hopCount);
                }
            }

            if (forwardAddress == null)
            {
                _directoryInstruments.RegistrationsSingleActLocal.Add(1);

                var result = DirectoryPartition.AddSingleActivation(address, previousAddress);

                // 更新缓存，使下一次本地查找直接在缓存中找到此 ActivationAddress，省去完整查找。
                DirectoryCache.AddOrUpdate(result.Address!, result.VersionTag);
                return result;
            }
            else
            {
                _directoryInstruments.RegistrationsSingleActRemoteSent.Add(1);

                // 否则，通知所有者。
                AddressAndTag result = await GetDirectoryReference(forwardAddress).RegisterAsync(address, previousAddress, hopCount + 1);

                // 缓存优化：
                // 仅当成功的 RegisterSingleActivation 调用所注册的不是重复激活实例时，才缓存其结果。
                // 这样，下次本地查找就会在缓存中找到此 ActivationAddress，省去完整查找！
                if (result.Address == null) return result;

                if (!address.Equals(result.Address) || IsDefunctActivation(address, clusterMembershipService.CurrentSnapshot)) return result;

                // 更新缓存，使下一次本地查找直接在缓存中找到此 ActivationAddress，省去完整查找。
                DirectoryCache.AddOrUpdate(result.Address, result.VersionTag);

                return result;
            }
        }

        public async Task UnregisterAfterNonexistingActivation(GrainAddress addr, SiloAddress origin)
        {
            LogTraceUnregisterAfterNonexistingActivation(addr, origin);

            await RefreshMembershipIfNewer(addr.MembershipVersion);

            if (origin == null || this.directoryMembership.MembershipCache.Contains(origin))
            {
                // 请求来自本集群，在本地调用注销操作。
                await UnregisterAsync(addr, UnregistrationCause.NonexistentActivation, 0);
            }
            else
            {
                // 请求来自另一个集群，在那个集群调用注销操作。
                var remoteDirectory = GetDirectoryReference(origin);
                await remoteDirectory.UnregisterAsync(addr, UnregistrationCause.NonexistentActivation);
            }
        }

        public async Task UnregisterAsync(GrainAddress address, UnregistrationCause cause, int hopCount)
        {
            if (hopCount > 0)
            {
                _directoryInstruments.UnregistrationsRemoteReceived.Add(1);
            }
            else
            {
                _directoryInstruments.UnregistrationsIssued.Add(1);
            }

            if (hopCount == 0)
                InvalidateCacheEntry(address);

            await RefreshMembershipIfNewer(address.MembershipVersion);

            // 检查目录分区的所有者是否为其他 Silo（如果当前 Silo 是所有者，则返回 null）。
            var forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, "UnregisterAsync");

            // 如果目录分区所有权再次发生变化，首次接收转发请求的 Silo（hopCount == 1）会立即继续转发。
            // 请求已经被再次转发后，先暂停并重新检查，再决定是否继续转发。
            if (hopCount > 1 && forwardAddress != null)
            {
                await Task.Delay(RETRY_DELAY);
                forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, "UnregisterAsync");
                LogWarningUnregisterAsyncNotOwner(
                    address,
                    forwardAddress,
                    hopCount);
            }

            if (forwardAddress == null)
            {
                // 当前 Silo 是目录分区的所有者。
                _directoryInstruments.UnregistrationsLocal.Add(1);
                DirectoryPartition.RemoveActivation(address.GrainId, address.ActivationId, cause);
            }
            else
            {
                _directoryInstruments.UnregistrationsRemoteSent.Add(1);
                // 否则，通知所有者。
                await GetDirectoryReference(forwardAddress).UnregisterAsync(address, cause, hopCount + 1);
            }
        }

        // 辅助方法，用于避免 UnregisterManyAsync 中重复代码。
        private void UnregisterOrPutInForwardList(List<GrainAddress> addresses, UnregistrationCause cause, int hopCount,
            ref Dictionary<SiloAddress, List<GrainAddress>>? forward, string context)
        {
            foreach (var address in addresses)
            {
                // 检查目录分区的所有者是否为其他 Silo（如果当前 Silo 是所有者，则返回 null）。
                var forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, context);

                if (forwardAddress != null)
                {
                    forward ??= new();
                    if (!forward.TryGetValue(forwardAddress, out var list))
                        forward[forwardAddress] = list = new();
                    list.Add(address);
                }
                else
                {
                    // 当前 Silo 是目录分区的所有者。
                    _directoryInstruments.UnregistrationsLocal.Add(1);

                    DirectoryPartition.RemoveActivation(address.GrainId, address.ActivationId, cause);
                }
            }
        }

        public async Task UnregisterManyAsync(List<GrainAddress> addresses, UnregistrationCause cause, int hopCount)
        {
            if (hopCount > 0)
            {
                _directoryInstruments.UnregistrationsManyRemoteReceived.Add(1);
            }
            else
            {
                _directoryInstruments.UnregistrationsManyIssued.Add(1);
            }

            await RefreshMembershipIfNewer(addresses);

            Dictionary<SiloAddress, List<GrainAddress>>? forwardlist = null;

            UnregisterOrPutInForwardList(addresses, cause, hopCount, ref forwardlist, "UnregisterManyAsync");

            // 如果目录分区所有权再次发生变化，首次接收转发请求的 Silo（hopCount == 1）会立即继续转发。
            // 请求已经被再次转发后，先暂停并重新检查，再决定是否继续转发。
            if (hopCount > 1 && forwardlist != null)
            {
                await Task.Delay(RETRY_DELAY);
                Dictionary<SiloAddress, List<GrainAddress>>? forwardlist2 = null;
                UnregisterOrPutInForwardList(addresses, cause, hopCount, ref forwardlist2, "UnregisterManyAsync");
                forwardlist = forwardlist2;
                if (forwardlist != null)
                {
                    LogWarningUnregisterManyAsyncNotOwner(
                        forwardlist.Count,
                        hopCount);
                }
            }

            // 转发请求。
            if (forwardlist != null)
            {
                var tasks = new List<Task>();
                foreach (var kvp in forwardlist)
                {
                    _directoryInstruments.UnregistrationsManyRemoteSent.Add(1);
                    tasks.Add(GetDirectoryReference(kvp.Key).UnregisterManyAsync(kvp.Value, cause, hopCount + 1));
                }

                // 等待所有请求完成。
                await Task.WhenAll(tasks);
            }
        }


        public AddressAndTag GetLocalDirectoryData(GrainId grain) => DirectoryPartition.LookUpActivation(grain);

        public bool TryLocalLookup(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address)
        {
            _directoryInstruments.LookupsLocalIssued.Add(1);
            if (TryCachedLookup(grainId, out address))
            {
                _directoryInstruments.LookupsLocalSuccesses.Add(1);
                return true;
            }

            var owner = CalculateGrainDirectoryPartition(grainId, this.directoryMembership);
            if (!MyAddress.Equals(owner))
            {
                address = null;
                return false;
            }

            _directoryInstruments.LookupsLocalDirectoryIssued.Add(1);
            var localResult = DirectoryPartition.LookUpActivation(grainId);
            if (localResult.Address is null)
            {
                address = null;
                return false;
            }

            address = localResult.Address;
            _directoryInstruments.LookupsLocalDirectorySuccesses.Add(1);
            _directoryInstruments.LookupsLocalSuccesses.Add(1);
            return true;
        }

        public GrainAddress? GetLocalCacheData(GrainId grain)
        {
            if (!DirectoryCache.LookUp(grain, out var cache))
            {
                return null;
            }

            return IsDefunctActivation(cache, clusterMembershipService.CurrentSnapshot) ? null : cache;
        }

        public async Task<AddressAndTag> LookupAsync(GrainId grainId, int hopCount = 0)
        {
            if (hopCount > 0)
            {
                _directoryInstruments.LookupsRemoteReceived.Add(1);
            }
            else
            {
                _directoryInstruments.LookupsFullIssued.Add(1);
            }

            // 检查目录分区的所有者是否为其他 Silo（如果当前 Silo 是所有者，则返回 null）。
            var forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "LookUpAsync");

            // 如果目录分区所有权再次发生变化，首次接收转发请求的 Silo（hopCount == 1）会立即继续转发。
            // 请求已经被再次转发后，先暂停并重新检查，再决定是否继续转发。
            if (hopCount > 1 && forwardAddress != null)
            {
                await Task.Delay(RETRY_DELAY);
                forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "LookUpAsync");
                if (forwardAddress is not null)
                {
                    int hash = unchecked((int)grainId.GetUniformHashCode());
                    LogWarningLookupAsyncNotOwner(
                        grainId,
                        hash,
                        forwardAddress,
                        hopCount);
                }
            }

            if (forwardAddress is not null && !IsValidSilo(forwardAddress))
            {
                await RefreshMembershipIfNewer(clusterMembershipService.CurrentSnapshot.Version);
                forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "LookUpAsync");
            }

            if (forwardAddress == null)
            {
                // 当前 Silo 是目录分区的所有者。
                _directoryInstruments.LookupsLocalDirectoryIssued.Add(1);
                var localResult = DirectoryPartition.LookUpActivation(grainId);
                if (localResult.Address == null)
                {
                    // 如果最近发生了成员资格变更，可能无法在当前分区中找到 Grain，
                    // 这是可能出现的情况。
                    LogTraceFullLookupMineNone(grainId);
                    return new(default, GrainInfo.NO_ETAG);
                }

                LogTraceFullLookupMine(grainId, localResult.Address);
                _directoryInstruments.LookupsLocalDirectorySuccesses.Add(1);
                return localResult;
            }
            else
            {
                // 仅为优化：无需向已知无效的节点发送消息。
                if (!IsValidSilo(forwardAddress))
                {
                    throw new OrleansException($"Current directory at {MyAddress} is not stable to perform the lookup for grainId {grainId} (it maps to {forwardAddress}, which is not a valid silo). Retry later.");
                }

                _directoryInstruments.LookupsRemoteSent.Add(1);
                var result = await GetDirectoryReference(forwardAddress).LookupAsync(grainId, hopCount + 1);

                // 更新缓存。
                if (result.Address is { } address && !IsDefunctActivation(address, clusterMembershipService.CurrentSnapshot))
                {
                    DirectoryCache.AddOrUpdate(address, result.VersionTag);
                }

                LogTraceFullLookupRemote(grainId, result.Address);

                return result;
            }
        }

        public async Task DeleteGrainAsync(GrainId grainId, int hopCount)
        {
            // 检查目录分区的所有者是否为其他 Silo（如果当前 Silo 是所有者，则返回 null）。
            var forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "DeleteGrainAsync");

            // 如果目录分区所有权再次发生变化，首次接收转发请求的 Silo（hopCount == 1）会立即继续转发。
            // 请求已经被再次转发后，先暂停并重新检查，再决定是否继续转发。
            if (hopCount > 1 && forwardAddress != null)
            {
                await Task.Delay(RETRY_DELAY);
                forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "DeleteGrainAsync");
                LogWarningDeleteGrainAsyncNotOwner(
                    grainId,
                    forwardAddress,
                    hopCount);
            }

            if (forwardAddress == null)
            {
                // 当前 Silo 是目录分区的所有者。
                DirectoryPartition.RemoveGrain(grainId);
            }
            else
            {
                // 否则，通知所有者。
                DirectoryCache.Remove(grainId);
                await GetDirectoryReference(forwardAddress).DeleteGrainAsync(grainId, hopCount + 1);
            }
        }

        public void InvalidateCacheEntry(GrainId grainId)
        {
            DirectoryCache.Remove(grainId);
        }

        public void InvalidateCacheEntry(GrainAddress activationAddress)
        {
            DirectoryCache.Remove(activationAddress);
        }

        /// <summary>
        /// 仅用于测试。
        /// 返回当前 Silo 认为是给定 Grain ID 的目录信息
        /// 主要所有者的 Silo。
        /// </summary>
        /// <param name="grain"></param>
        /// <returns></returns>
        public SiloAddress? GetPrimaryForGrain(GrainId grain) => CalculateGrainDirectoryPartition(grain);

        private long RingDistanceToSuccessor() => FindSuccessor(MyAddress) is { } successor ? CalcRingDistance(MyAddress, successor) : 0;

        private static long CalcRingDistance(SiloAddress silo1, SiloAddress silo2)
        {
            const long ringSize = int.MaxValue * 2L;
            long hash1 = silo1.GetConsistentHashCode();
            long hash2 = silo2.GetConsistentHashCode();

            if (hash2 > hash1) return hash2 - hash1;
            if (hash2 < hash1) return ringSize - (hash1 - hash2);

            return 0;
        }

        internal IRemoteGrainDirectory GetDirectoryReference(SiloAddress silo)
        {
            return this.grainFactory.GetSystemTarget<IRemoteGrainDirectory>(Constants.DirectoryServiceType, silo);
        }

        private bool IsSiloNextInTheRing(SiloAddress siloAddr, int hash, bool excludeMySelf)
        {
            return siloAddr.GetConsistentHashCode() <= hash && (!excludeMySelf || !siloAddr.Equals(MyAddress));
        }

        private static int CompareSiloAddress(SiloAddress left, SiloAddress right)
        {
            var hashComparison = left.GetConsistentHashCode().CompareTo(right.GetConsistentHashCode());
            return hashComparison != 0 ? hashComparison : left.CompareTo(right);
        }

        public void AddOrUpdateCacheEntry(GrainId grainId, SiloAddress siloAddress) => this.DirectoryCache.AddOrUpdate(new GrainAddress { GrainId = grainId, SiloAddress = siloAddress }, 0);
        public bool TryCachedLookup(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address)
        {
            _directoryInstruments.LookupsCacheIssued.Add(1);
            if ((address = GetLocalCacheData(grainId)) is null)
            {
                return false;
            }

            _directoryInstruments.LookupsCacheSuccesses.Add(1);
            return true;
        }
        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe<LocalGrainDirectory>(ServiceLifecycleStage.RuntimeServices, RunStart, RunStop);

            Task RunStart(CancellationToken ct) => Task.Run(() => Start(), CancellationToken.None);
            Task RunStop(CancellationToken ct) => Task.Run(() => StopAsync(), CancellationToken.None);
        }

        private readonly struct SiloAddressLogValue(SiloAddress silo)
        {
            public override string ToString() => silo.ToStringWithHashCode();
        }

        private readonly struct GrainHashLogValue(GrainId grain)
        {
            public override string ToString() => grain.GetUniformHashCode().ToString();
        }

        private readonly struct SiloHashLogValue(SiloAddress? silo)
        {
            public override string ToString() => silo?.GetConsistentHashCode().ToString() ?? "null";
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Start"
        )]
        private partial void LogDebugStart();

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Silo {SiloAddress} added silo {OtherSiloAddress}"
        )]
        private partial void LogDebugSiloAddedSilo(SiloAddress siloAddress, SiloAddress otherSiloAddress);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.Catalog_SiloStatusChangeNotification_Exception,
            Message = "LocalGrainDirectory has thrown an exception while handling removal of silo {Silo}."
        )]
        private partial void LogErrorSiloStatusChangeNotification(SiloAddressLogValue silo, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.Catalog_SiloStatusChangeNotification,
            Message = "LocalGrainDirectory is deactivating {Count} activations because silo {Silo} entered status {Status} and was the primary directory partition for these grain ids."
        )]
        private partial void LogInfoSiloStatusChangeNotification(int count, SiloAddressLogValue silo, SiloStatus status);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.Catalog_DeactivateActivation_Exception,
            Message = "LocalGrainDirectory has thrown an exception while deactivating activation {GrainId} due to removal of silo {Silo}."
        )]
        private partial void LogErrorDeactivatingActivationForRemovedSilo(Exception exception, GrainId grainId, SiloAddressLogValue silo);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error processing cluster membership updates."
        )]
        private partial void LogErrorProcessingMembershipUpdates(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to refresh cluster membership after a directory membership update processing error."
        )]
        private partial void LogWarningRefreshingClusterMembershipFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Failed to dispose the grain directory cache."
        )]
        private partial void LogWarningDisposeDirectoryCacheFailed(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Silo {LocalSilo} removed silo {OtherSilo}"
        )]
        private partial void LogDebugSiloRemovedSilo(SiloAddress localSilo, SiloAddress otherSilo);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100201,
            Level = LogLevel.Warning,
            Message = "Got request to find predecessors of silo {SiloAddress}, which is not in the list of members"
        )]
        private partial void LogWarningFindPredecessorSiloNotInList(SiloAddress siloAddress);

        [LoggerMessage(
            EventId = (int)ErrorCode.Runtime_Error_100203,
            Level = LogLevel.Warning,
            Message = "Got request to find successors of silo {SiloAddress}, which is not in the list of members"
        )]
        private partial void LogWarningFindSuccessorSiloNotInList(SiloAddress siloAddress);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Silo {SiloAddress} looked for a system target {GrainId}, returned {TargetSilo}"
        )]
        private partial void LogTraceSystemTargetLookup(SiloAddress siloAddress, GrainId grainId, SiloAddress targetSilo);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Silo {SiloAddress} calculated directory partition owner silo {OwnerAddress} for grain {GrainId}: {GrainIdHash} --> {OwnerAddressHash}"
        )]
        private partial void LogTraceCalculatedDirectoryPartitionOwner(SiloAddress siloAddress, SiloAddress? ownerAddress, GrainId grainId, int grainIdHash, long? ownerAddressHash);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "RegisterAsync - It seems we are not the owner of activation {Address} (hash: {Hash:X}), trying to forward it to {ForwardAddress} (hopCount={HopCount})"
        )]
        private partial void LogWarningRegisterAsyncNotOwner(GrainAddress address, int hash, SiloAddress forwardAddress, int hopCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "UnregisterAfterNonexistingActivation addr={Address} origin={Origin}"
        )]
        private partial void LogTraceUnregisterAfterNonexistingActivation(GrainAddress address, SiloAddress origin);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "UnregisterAsync - It seems we are not the owner of activation {Address}, trying to forward it to {ForwardAddress} (hopCount={HopCount})"
        )]
        private partial void LogWarningUnregisterAsyncNotOwner(GrainAddress address, SiloAddress? forwardAddress, int hopCount);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "RegisterAsync - It seems we are not the owner of some activations, trying to forward it to {Count} silos (hopCount={HopCount})"
        )]
        private partial void LogWarningUnregisterManyAsyncNotOwner(int count, int hopCount);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "LookupAsync - It seems we are not the owner of grain {GrainId} (hash: {Hash:X}), trying to forward it to {ForwardAddress} (hopCount={HopCount})"
        )]
        private partial void LogWarningLookupAsyncNotOwner(GrainId grainId, int hash, SiloAddress forwardAddress, int hopCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "FullLookup mine {GrainId}=none"
        )]
        private partial void LogTraceFullLookupMineNone(GrainId grainId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "FullLookup mine {GrainId}={Address}"
        )]
        private partial void LogTraceFullLookupMine(GrainId grainId, GrainAddress? address);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "FullLookup remote {GrainId}={Address}"
        )]
        private partial void LogTraceFullLookupRemote(GrainId grainId, GrainAddress? address);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "DeleteGrainAsync - It seems we are not the owner of grain {GrainId}, trying to forward it to {ForwardAddress} (hopCount={HopCount})"
        )]
        private partial void LogWarningDeleteGrainAsyncNotOwner(GrainId grainId, SiloAddress? forwardAddress, int hopCount);

        private class DirectoryMembership(ImmutableList<SiloAddress> membershipRingList, ImmutableHashSet<SiloAddress> membershipCache)
        {
            public static DirectoryMembership Default { get; } = new DirectoryMembership([], []);

            public static DirectoryMembership Create(IEnumerable<SiloAddress> members)
            {
                var builder = ImmutableList.CreateBuilder<SiloAddress>();
                builder.AddRange(members);
                builder.Sort(CompareSiloAddress);
                var ring = builder.ToImmutable();
                return new DirectoryMembership(ring, [.. ring]);
            }

            public ImmutableList<SiloAddress> MembershipRingList { get; } = membershipRingList;
            public ImmutableHashSet<SiloAddress> MembershipCache { get; } = membershipCache;
        }
    }
}
