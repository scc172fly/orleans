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
    // 因为此类型用作字典的键，所以重写相等性比较和哈希码计算；
    // 记录结构体使用默认相等性比较器，而该比较器对枚举而言性能不理想。
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
            // _migratableStatuses 保存各 Grain 类型是否可迁移的状态，以便快速查找。
            // 考虑到预计 Grain 类型的数量不会太多，使用此结构进行快速检查是合理的。
            if (!_migratableStatuses.TryGetValue(statusKey, out var isMigratable))
            {
                isMigratable = !(grainType.IsClient() || grainType.IsSystemTarget() || grainType.IsGrainService() || IsStatelessWorker(grainType) || IsImmovable(grainType));
                _migratableStatuses.TryAdd(statusKey, isMigratable);
            }

            // 定期重新生成缓存。
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
            // 与 GrainPropertiesResolver 一样，清单使用未构造的类型描述泛型 Grain 类。
            var lookupKey = GenericGrainType.TryParse(grainType, out var generic) ? generic.GetUnconstructedGrainType().GrainType : grainType;
            if (_localManifest.Grains.TryGetValue(lookupKey, out var props))
            {
                // 如果没有 Immovable 属性，则该类型并非不可移动。
                if (!props.Properties.TryGetValue(WellKnownGrainTypeProperties.Immovable, out var value))
                {
                    return false;
                }

                // 如果无法解析该值，则假定其不可移动。
                if (!byte.TryParse(value, out var actualKindValue))
                {
                    return true;
                }

                // 该类型不可移动，但其类别是否与参数匹配？
                return ((ImmovableKind)actualKindValue & expectedKind) == expectedKind;
            }

            // 假定未知 Grain 不可移动。
            return true;
        }
    }
}
