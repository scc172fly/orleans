using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Providers;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Orleans.Runtime.Versions
{
    internal interface IVersionStoreGrain : IGrainWithStringKey
    {
        [Alias("7261373F")] Task<Dictionary<GrainInterfaceType, CompatibilityStrategy>> GetCompatibilityStrategies(CancellationToken cancellationToken = default);
        [Alias("743D88ED")] Task<Dictionary<GrainInterfaceType, VersionSelectorStrategy>> GetSelectorStrategies(CancellationToken cancellationToken = default);
        [Alias("67EF9A39")] Task<CompatibilityStrategy?> GetCompatibilityStrategy(CancellationToken cancellationToken = default);
        [Alias("8A72848A")] Task<VersionSelectorStrategy?> GetSelectorStrategy(CancellationToken cancellationToken = default);
        [Alias("67A0B5AA")] Task SetCompatibilityStrategy(CompatibilityStrategy strategy, CancellationToken cancellationToken = default);
        [Alias("E7532DE3")] Task SetSelectorStrategy(VersionSelectorStrategy strategy, CancellationToken cancellationToken = default);
        [Alias("1B7F13C8")] Task SetCompatibilityStrategy(GrainInterfaceType interfaceType, CompatibilityStrategy strategy, CancellationToken cancellationToken = default);
        [Alias("3E6DDE3E")] Task SetSelectorStrategy(GrainInterfaceType interfaceType, VersionSelectorStrategy strategy, CancellationToken cancellationToken = default);
    }

    [GenerateSerializer]
    internal sealed class VersionStoreGrainState
    {
        [Id(0)]
        public readonly Dictionary<GrainInterfaceType, CompatibilityStrategy> CompatibilityStrategies = new();
        [Id(1)]
        public readonly Dictionary<GrainInterfaceType, VersionSelectorStrategy> VersionSelectorStrategies = new();
        [Id(2)]
        public VersionSelectorStrategy? SelectorOverride;
        [Id(3)]
        public CompatibilityStrategy? CompatibilityOverride;
    }

    [StorageProvider(ProviderName = ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME)]
    internal class VersionStoreGrain : Grain<VersionStoreGrainState>, IVersionStoreGrain
    {
        public async Task SetCompatibilityStrategy(
            CompatibilityStrategy strategy,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.State!.CompatibilityOverride = strategy; // 在分发 Grain 调用之前，已初始化 Grain 状态。
            await this.WriteStateAsync();
        }

        public async Task SetSelectorStrategy(
            VersionSelectorStrategy strategy,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.State!.SelectorOverride = strategy; // 在分发 Grain 调用之前，已初始化 Grain 状态。
            await this.WriteStateAsync();
        }

        public async Task SetCompatibilityStrategy(
            GrainInterfaceType ifaceId,
            CompatibilityStrategy strategy,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.State!.CompatibilityStrategies[ifaceId] = strategy; // 在分发 Grain 调用之前，已初始化 Grain 状态。
            await this.WriteStateAsync();
        }

        public async Task SetSelectorStrategy(
            GrainInterfaceType ifaceId,
            VersionSelectorStrategy strategy,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.State!.VersionSelectorStrategies[ifaceId] = strategy; // 在分发 Grain 调用之前，已初始化 Grain 状态。
            await this.WriteStateAsync();
        }

        public bool IsEnabled { get; }

        public Task<Dictionary<GrainInterfaceType, CompatibilityStrategy>> GetCompatibilityStrategies(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.State!.CompatibilityStrategies); // 在分发 Grain 调用之前，已初始化 Grain 状态。
        }

        public Task<Dictionary<GrainInterfaceType, VersionSelectorStrategy>> GetSelectorStrategies(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.State!.VersionSelectorStrategies); // 在分发 Grain 调用之前，已初始化 Grain 状态。
        }

        public Task<CompatibilityStrategy?> GetCompatibilityStrategy(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.State!.CompatibilityOverride); // 在分发 Grain 调用之前，已初始化 Grain 状态。
        }

        public Task<VersionSelectorStrategy?> GetSelectorStrategy(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.State!.SelectorOverride); // 在分发 Grain 调用之前，已初始化 Grain 状态。
        }
    }
}
