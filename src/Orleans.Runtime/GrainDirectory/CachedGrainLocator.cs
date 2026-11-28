using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;
using Orleans.Internal;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 实现 <see cref="IGrainLocator"/>，使用 <see cref="IGrainDirectory"/> 存储。
    /// </summary>
    internal class CachedGrainLocator : IGrainLocator, ILifecycleParticipant<ISiloLifecycle>, CachedGrainLocator.ITestAccessor
    {
        private readonly GrainDirectoryResolver grainDirectoryResolver;
        private readonly IGrainDirectoryCache cache;
        private readonly bool disposeCache;
        private readonly DirectoryInstruments _directoryInstruments;

        private readonly CancellationTokenSource shutdownToken = new CancellationTokenSource();
        private readonly IClusterMembershipService clusterMembershipService;

        private Task? listenToClusterChangeTask;

        internal interface ITestAccessor
        {
            MembershipVersion LastMembershipVersion { get; set; }
        }

        MembershipVersion ITestAccessor.LastMembershipVersion { get; set; }

        public CachedGrainLocator(
            IServiceProvider serviceProvider,
            GrainDirectoryResolver grainDirectoryResolver,
            IClusterMembershipService clusterMembershipService,
            DirectoryInstruments directoryInstruments,
            IOptions<GrainDirectoryOptions> grainDirectoryOptions)
        {
            this.grainDirectoryResolver = grainDirectoryResolver;
            this.clusterMembershipService = clusterMembershipService;
            _directoryInstruments = directoryInstruments;
            this.cache = GrainDirectoryCacheFactory.CreateCustomGrainDirectoryCache(serviceProvider, grainDirectoryOptions.Value, out this.disposeCache, _directoryInstruments);
        }

        public async ValueTask<GrainAddress?> Lookup(GrainId grainId)
        {
            var grainType = grainId.Type;
            if (grainType.IsClient() || grainType.IsSystemTarget())
            {
                ThrowUnsupportedGrainType(grainId);
            }

            // 首先检查缓存。
            if (TryLookupInCache(grainId, out var cachedResult))
            {
                return cachedResult;
            }

            var entry = await GetGrainDirectory(grainId.Type).Lookup(grainId);

            // 未找到条目。
            if (entry is null)
            {
                return null;
            }

            // 检查条目是否指向已死亡的 Silo。
            if (IsKnownDeadSilo(entry))
            {
                // 将其从目录移除。
                await GetGrainDirectory(grainId.Type).Unregister(entry);
                entry = null;
            }
            else
            {
                // 添加到本地缓存并返回。
                this.cache.AddOrUpdate(entry, 0);
            }

            return entry;
        }

        public Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress) =>
            Register(address, previousAddress, CancellationToken.None);

        internal async Task<GrainAddress?> Register(
            GrainAddress address,
            GrainAddress? previousAddress,
            CancellationToken cancellationToken)
        {
            var grainType = address.GrainId.Type;
            if (grainType.IsClient() || grainType.IsSystemTarget())
            {
                ThrowUnsupportedGrainType(address.GrainId);
            }

            address = new()
            {
                GrainId = address.GrainId,
                ActivationId = address.ActivationId,
                SiloAddress = address.SiloAddress,
                MembershipVersion = clusterMembershipService.CurrentSnapshot.Version
            };

            var directory = GetGrainDirectory(grainType);
            var result = await directory.Register(address, previousAddress, cancellationToken);

            if (result is null)
            {
                // 若注册失败，则返回 null 地址。
                return null;
            }

            // 检查条目是否指向已死亡的 Silo。
            if (IsKnownDeadSilo(result))
            {
                // 有条件地替换过时条目。
                result = await directory.Register(address, result, cancellationToken);
            }

            // 更新缓存。
            this.cache.AddOrUpdate(result!, (int)result!.MembershipVersion.Value);

            return result;

        }

        public async Task Unregister(GrainAddress address, UnregistrationCause cause)
        {
            // 首先从本地缓存移除，以免再次返回该条目。
            this.cache.Remove(address);

            // 从 Grain 目录移除条目，这可能耗费明显更长的时间。
            await GetGrainDirectory(address.GrainId.Type).Unregister(address);

            // 查找操作可能与 Unregister 发生竞态，将无效条目重新添加到缓存。
            if (this.cache.LookUp(address.GrainId, out var entry, out _) && entry.Equals(address))
            {
                this.cache.Remove(address);
            }
        }

        public void Participate(ISiloLifecycle lifecycle)
        {
            Task OnStart(CancellationToken ct)
            {
                this.listenToClusterChangeTask = ListenToClusterChange();
                return Task.CompletedTask;
            }
            ;
            async Task OnStop(CancellationToken ct)
            {
                this.shutdownToken.Cancel();
                if (listenToClusterChangeTask != default && !ct.IsCancellationRequested)
                {
                    await listenToClusterChangeTask.WaitAsync(ct).SuppressThrowing();
                }

                if (this.disposeCache)
                {
                    await GrainDirectoryCacheFactory.DisposeGrainDirectoryCacheAsync(this.cache);
                }
            }
            ;
            lifecycle.Subscribe(nameof(CachedGrainLocator), ServiceLifecycleStage.RuntimeGrainServices, OnStart, OnStop);
        }

        private IGrainDirectory GetGrainDirectory(GrainType grainType) => this.grainDirectoryResolver.Resolve(grainType)!;

        private async Task ListenToClusterChange()
        {
            var previousSnapshot = this.clusterMembershipService.CurrentSnapshot;

            ((ITestAccessor)this).LastMembershipVersion = previousSnapshot.Version;

            var updates = this.clusterMembershipService.MembershipUpdates.WithCancellation(this.shutdownToken.Token);
            await foreach (var snapshot in updates)
            {
                // 主动筛选：检测已死亡的 Silo，并尝试主动清理目录。
                var changes = snapshot.CreateUpdate(previousSnapshot).Changes;
                var deadSilos = changes
                    .Where(member => member.Status == SiloStatus.Dead)
                    .Select(member => member.SiloAddress)
                    .ToList();

                if (deadSilos.Count > 0)
                {
                    var tasks = new List<Task>();
                    foreach (var directory in this.grainDirectoryResolver.Directories)
                    {
                        tasks.Add(directory.UnregisterSilos(deadSilos));
                    }
                    await Task.WhenAll(tasks).WaitAsync(this.shutdownToken.Token);
                }

                ((ITestAccessor)this).LastMembershipVersion = snapshot.Version;
                previousSnapshot = snapshot;
            }
        }

        private bool IsKnownDeadSilo(GrainAddress grainAddress)
            => IsKnownDeadSilo(grainAddress.SiloAddress, grainAddress.MembershipVersion);

        private bool IsKnownDeadSilo(SiloAddress? siloAddress, MembershipVersion membershipVersion)
        {
            var current = this.clusterMembershipService.CurrentSnapshot;
            return siloAddress is null || current.GetSiloStatus(siloAddress, membershipVersion) == SiloStatus.Dead;
        }

        private static void ThrowUnsupportedGrainType(GrainId grainId) => throw new InvalidOperationException($"Unsupported grain type for grain {grainId}");

        public void UpdateCache(GrainId grainId, SiloAddress siloAddress)
        {
            var membershipVersion = this.clusterMembershipService.CurrentSnapshot.Version;
            cache.AddOrUpdate(new GrainAddress { GrainId = grainId, SiloAddress = siloAddress, MembershipVersion = membershipVersion }, (int)membershipVersion.Value);
        }
        public void InvalidateCache(GrainId grainId) => cache.Remove(grainId);
        public void InvalidateCache(GrainAddress address) => cache.Remove(address);
        public bool TryLookupInCache(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address)
        {
            var grainType = grainId.Type;
            if (grainType.IsClient() || grainType.IsSystemTarget())
            {
                ThrowUnsupportedGrainType(grainId);
            }

            _directoryInstruments.LookupsCacheIssued.Add(1);
            if (this.cache.LookUp(grainId, out address, out _))
            {
                // 如果 Silo 已死亡，则移除条目。
                if (IsKnownDeadSilo(address))
                {
                    address = default;
                    this.cache.Remove(grainId);
                }
                else
                {
                    // 已找到有效条目，返回该条目。
                    _directoryInstruments.LookupsCacheSuccesses.Add(1);
                    return true;
                }
            }

            return false;
        }
    }
}
