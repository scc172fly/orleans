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

        // 考虑:将这些常数移到一个合适的地方
        internal const int HOP_LIMIT = 6; // 远端请求, 其前向请求最多不超过5 次
        public static readonly TimeSpan RETRY_DELAY = TimeSpan.FromMilliseconds(200); // 暂停二百米之间间隔,让会员名册稳定下来
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

            // 遗留远程端点为默认目录服务。命名的目录使用自己的端点。
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

        // 请注意,当提出停止事件时,执行程序将停止处理目录变更请求(登记、不登记等)。
        // 这意味着,在一段短暂的时期内, silo 认为它拥有一套
        // grains (供更新之用),可导致申请请求要求为暂停使用而需要重新启动。
        // 另一种办法是允许 silo 在它交出分部分分配后处理请求,在这种情况下这些改动
        // 将获得成功的答复,但不会反映在名录的最终状态中。
        // 改变这一点很容易 如果我们觉得交换比较好
        public async Task StopAsync()
        {
            // 这将导致远程书写请求被转发给 silo 将成为新的主人。
            // 随着会员国稳定下来,请求可能会有一段时间反弹,但两个理事国将随时向这些请求提供服务。
            // 新拥有者 grain或最终失败。 silo 等我们
            // 开始停止,这可能导致他们不被交给新主人。

            //以假身份运行的标记将排除我自己 CalculateGrainDirectoryPartition(grainId)
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
                        // 总是应用最新快照 。
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

                // 移除属于我的条目。 它们应该从我目录分区中检索 。
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
        /// 查找 silo 的目录信息 grain ID.
        /// 只有我是唯一的 silo 并关闭
        /// </summary>
        /// <param name="grainId"></param>
        /// <returns></returns>
        public SiloAddress? CalculateGrainDirectoryPartition(GrainId grainId)
            => CalculateGrainDirectoryPartition(grainId, this.directoryMembership);

        private SiloAddress? CalculateGrainDirectoryPartition(GrainId grainId, DirectoryMembership membership)
        {
            // 给予特殊 grains特别待遇
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

                // 每个 silo 都有其系统目标
                return MyAddress;
            }

            SiloAddress? siloAddress = null;
            int hash = unchecked((int)grainId.GetUniformHashCode());

            // excludeMySelf 开始为 TargetSilo 如果我们不运行, excludeThisSIloIfStopping 标记为是真实的。请在“停止”方法中看到注释。
            // excludeThisSIloIfStopping 国旗被删除是因为我们认为旗帜使事情变得不必要的复杂。 如果旗子被印出来,我们可以把它补回来
            // 做一些有价值的事
            bool excludeMySelf = !Running;

            var existing = membership;
            if (existing.MembershipRingList.Count == 0)
            {
                // 如果会员圈空了 我们就是默认的所有人 除非我们停止
                return !Running ? null : MyAddress;
            }

            // 需要执行二进搜索, 但目前只需跳过按其杂列排序的 silos 列表
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
                // 如果在十字路口没有发现,则最后的 silo 将完成(我们是在环形上)。
                // 我们在上面检查了一下 以确保名单不是空的 所以这应该永远是安全的
                siloAddress = existing.MembershipRingList[existing.MembershipRingList.Count - 1];
                // 确定不是我们...
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
                // 要么我们不知道其他的 silos ,我们停止了,要么我们是业主。
                // Null表示,作业应在当地进行。
                // 如果该主机正在终止,则向此主机登记的任何 grain 都必须终止。
                return null;
            }

            if (hopCount >= HOP_LIMIT)
            {
                // 我们没有转发 因为已经有太多跳过
                throw new OrleansException($"Silo {MyAddress} is not owner of {grainId}, cannot forward {operationDescription} to owner {owner} because hop limit is reached");
            }

            // 转入 silo 我们认为是主人
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

            // 看看业主是否在别的地方(如果我们是业主,则返回无效)
            var forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, "RegisterAsync");

            // 第一个转发的所有人(hopCount = 1)如果所有权再次变更,立即转发。
            // 请求已经重新提出、暂停和重新核对后再予以撤回。
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

                // 更新缓存, 这样下个本地查找会发现这个 ActivationAddress 在缓存中, 我们会保存全部的搜索 。
                DirectoryCache.AddOrUpdate(result.Address!, result.VersionTag);
                return result;
            }
            else
            {
                _directoryInstruments.RegistrationsSingleActRemoteSent.Add(1);

                // 通知所有人,否则,通知
                AddressAndTag result = await GetDirectoryReference(forwardAddress).RegisterAsync(address, previousAddress, hopCount + 1);

                // 缓存优化 :
                // 成功调用 RegisterSingleActivation 的缓存结果, 只有当它不是重复激活时才使用。
                // 下一步本地搜索将会在缓存中找到这个 ActivationAddress , 我们会保存完整搜索!
                if (result.Address == null) return result;

                if (!address.Equals(result.Address) || IsDefunctActivation(address, clusterMembershipService.CurrentSnapshot)) return result;

                // 更新缓存, 这样下个本地查找会发现这个 ActivationAddress 在缓存中, 我们会保存全部的搜索 。
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
                // 请求来自本组中的请求,请在此拨打未登记
                await UnregisterAsync(addr, UnregistrationCause.NonexistentActivation, 0);
            }
            else
            {
                // 请求来自另一组,请打电话到那里不登记
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

            // 看看业主是否在别的地方(如果我们是业主,则返回无效)
            var forwardAddress = this.CheckIfShouldForward(address.GrainId, hopCount, "UnregisterAsync");

            // 第一个转发的所有人(hopCount = 1)如果所有权再次变更,立即转发。
            // 请求已经重新提出、暂停和重新核对后再予以撤回。
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
                // 我们是老板
                _directoryInstruments.UnregistrationsLocal.Add(1);
                DirectoryPartition.RemoveActivation(address.GrainId, address.ActivationId, cause);
            }
            else
            {
                _directoryInstruments.UnregistrationsRemoteSent.Add(1);
                // 通知所有人,否则,通知
                await GetDirectoryReference(forwardAddress).UnregisterAsync(address, cause, hopCount + 1);
            }
        }

        // 避免 UnregisterManyAsync内代码重复的辅助工具方法
        private void UnregisterOrPutInForwardList(List<GrainAddress> addresses, UnregistrationCause cause, int hopCount,
            ref Dictionary<SiloAddress, List<GrainAddress>>? forward, string context)
        {
            foreach (var address in addresses)
            {
                // 看看业主是否在别的地方(如果我们是业主,则返回无效)
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
                    // 我们是老板
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

            // 第一个转发的所有人(hopCount = 1)如果所有权再次变更,立即转发。
            // 请求已经重新提出、暂停和重新核对后再予以撤回。
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

            // 转发请求
            if (forwardlist != null)
            {
                var tasks = new List<Task>();
                foreach (var kvp in forwardlist)
                {
                    _directoryInstruments.UnregistrationsManyRemoteSent.Add(1);
                    tasks.Add(GetDirectoryReference(kvp.Key).UnregisterManyAsync(kvp.Value, cause, hopCount + 1));
                }

                // 等待所有请求完成
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

            // 看看业主是否在别的地方(如果我们是业主,则返回无效)
            var forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "LookUpAsync");

            // 第一个转发的所有人(hopCount = 1)如果所有权再次变更,立即转发。
            // 请求已经重新提出、暂停和重新核对后再予以撤回。
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
                // 我们是老板
                _directoryInstruments.LookupsLocalDirectoryIssued.Add(1);
                var localResult = DirectoryPartition.LookUpActivation(grainId);
                if (localResult.Address == null)
                {
                    // 发生这样的情况时,如果存在的话,我们无法在我们的分区中找到 grain
                    // 成员数目最近的一些变化
                    LogTraceFullLookupMineNone(grainId);
                    return new(default, GrainInfo.NO_ETAG);
                }

                LogTraceFullLookupMine(grainId, localResult.Address);
                _directoryInstruments.LookupsLocalDirectorySuccesses.Add(1);
                return localResult;
            }
            else
            {
                // 优化而已 为什么给某个我们认识的人发信息是无效的
                if (!IsValidSilo(forwardAddress))
                {
                    throw new OrleansException($"Current directory at {MyAddress} is not stable to perform the lookup for grainId {grainId} (it maps to {forwardAddress}, which is not a valid silo). Retry later.");
                }

                _directoryInstruments.LookupsRemoteSent.Add(1);
                var result = await GetDirectoryReference(forwardAddress).LookupAsync(grainId, hopCount + 1);

                // 更新缓存
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
            // 看看业主是否在别的地方(如果我们是业主,则返回无效)
            var forwardAddress = this.CheckIfShouldForward(grainId, hopCount, "DeleteGrainAsync");

            // 第一个转发的所有人(hopCount = 1)如果所有权再次变更,立即转发。
            // 请求已经重新提出、暂停和重新核对后再予以撤回。
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
                // 我们是老板
                DirectoryPartition.RemoveGrain(grainId);
            }
            else
            {
                // 通知所有人,否则,通知
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
        /// 返回 silo ,该 silo 认为它是目录信息的主要所有者
        /// 提供 grain ID。
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
