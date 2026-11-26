using Orleans.Metadata;
using Orleans.Placement;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;


namespace Orleans.Runtime.Placement;

internal sealed class GrainMigratabilityChecker(
    PlacementStrategyResolver strategyResolver,
    IClusterManifestProvider clusterManifestProvider,
    [FromKeyedServices(TimeProviderNames.ActivationManagement)] TimeProvider timeProvider)
{
    // 我们推翻平等和大麻法典,因为这种类型的文字典是用来作为字典的钥匙。
    // 并使用默认等同比较器记录结构,对于一个极值来说,对性能而言,它并不那么好。
    private readonly record struct StatusKey(GrainType Type, ImmovableKind Kind) : IEquatable<StatusKey>
    {
        public bool Equals(StatusKey other) => Type == other.Type && Kind == other.Kind;
        public override int GetHashCode() => HashCode.Combine(Type.GetUniformHashCode(), Kind);
    }

    private readonly GrainManifest _localManifest = clusterManifestProvider.LocalGrainManifest;
    private readonly PlacementStrategyResolver _strategyResolver = strategyResolver;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ConcurrentDictionary<StatusKey, bool> _migratableStatuses = new();
    private FrozenDictionary<StatusKey, bool>? _migratableStatusesCache;
    private long _lastRegeneratedCacheTimestamp = timeProvider.GetTimestamp();

    public bool IsMigratable(GrainType grainType, ImmovableKind expectedKind)
    {
        var statusKey = new StatusKey(grainType, expectedKind);
        if (_migratableStatusesCache is { } cache && cache.TryGetValue(statusKey, out var isMigratable))
        {
            return isMigratable;
        }

        return IsMigratableRare(grainType, statusKey);

        bool IsMigratableRare(GrainType grainType, StatusKey statusKey)
        {
            // 偏差类型是否为偏移型号,则每个类型 grain 都持有身份,因此我们可以快速查看。
            // 因为我们预计不会有大量的 grain *类型*,
            if (!_migratableStatuses.TryGetValue(statusKey, out var isMigratable))
            {
                isMigratable = !(grainType.IsClient() || grainType.IsSystemTarget() || grainType.IsGrainService() || IsStatelessWorker(grainType) || IsImmovable(grainType));
                _migratableStatuses.TryAdd(statusKey, isMigratable);
            }

            // 定期重新恢复缓存。
            var currentTimestamp = _timeProvider.GetTimestamp();
            if (_timeProvider.GetElapsedTime(_lastRegeneratedCacheTimestamp, currentTimestamp) > TimeSpan.FromSeconds(5))
            {
                _migratableStatusesCache = _migratableStatuses.ToFrozenDictionary();
                _lastRegeneratedCacheTimestamp = currentTimestamp;
            }

            return isMigratable;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsStatelessWorker(GrainType grainType) =>
            _strategyResolver.GetPlacementStrategy(grainType).GetType() == typeof(StatelessWorkerPlacement);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsImmovable(GrainType grainType)
        {
            // 清单按未构建类型描述通用 grain 类,如 GrainPropertiesResolver 一样。
            var lookupKey = GenericGrainType.TryParse(grainType, out var generic) ? generic.GetUnconstructedGrainType().GrainType : grainType;
            if (_localManifest.Grains.TryGetValue(lookupKey, out var props))
            {
                // 如果没有“不动产”,它就不是不动产。
                if (!props.Properties.TryGetValue(WellKnownGrainTypeProperties.Immovable, out var value))
                {
                    return false;
                }

                // 如果数值无法分析, 假设它是不动产。
                if (!byte.TryParse(value, out var actualKindValue))
                {
                    return true;
                }

                // 它是不动的, 但类型和参数匹配 。
                return ((ImmovableKind)actualKindValue & expectedKind) == expectedKind;
            }

            // 未知 grains 为不动产。
            return true;
        }
    }
}
