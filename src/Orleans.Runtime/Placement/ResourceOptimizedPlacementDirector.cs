using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Runtime.Placement;

// 参见：https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/
internal sealed class ResourceOptimizedPlacementDirector : IPlacementDirector, ISiloStatisticsChangeListener
{
    private const int FourKiloByte = 4096;
    private readonly SiloAddress _localSilo;
    private readonly NormalizedWeights _weights;
    private readonly float _localSiloPreferenceMargin;
    private readonly ConcurrentDictionary<SiloAddress, ResourceStatistics> _siloStatistics = [];
    private readonly Task<SiloAddress> _cachedLocalSilo;

    public ResourceOptimizedPlacementDirector(
        ILocalSiloDetails localSiloDetails,
        DeploymentLoadPublisher deploymentLoadPublisher,
        IOptions<ResourceOptimizedPlacementOptions> options)
    {
        _localSilo = localSiloDetails.SiloAddress;
        _cachedLocalSilo = Task.FromResult(_localSilo);
        _weights = NormalizeWeights(options.Value);
        _localSiloPreferenceMargin = (float)options.Value.LocalSiloPreferenceMargin / 100;
        deploymentLoadPublisher.SubscribeToStatisticsChangeEvents(this);
    }

    private static NormalizedWeights NormalizeWeights(ResourceOptimizedPlacementOptions input)
    {
        int totalWeight = input.CpuUsageWeight + input.MemoryUsageWeight + input.AvailableMemoryWeight + input.MaxAvailableMemoryWeight + input.ActivationCountWeight;

        return totalWeight == 0 ? new(0f, 0f, 0f, 0f, 0f) :
            new(
                CpuUsageWeight: (float)input.CpuUsageWeight / totalWeight,
                MemoryUsageWeight: (float)input.MemoryUsageWeight / totalWeight,
                AvailableMemoryWeight: (float)input.AvailableMemoryWeight / totalWeight,
                MaxAvailableMemoryWeight: (float)input.MaxAvailableMemoryWeight / totalWeight,
                ActivationCountWeight: (float)input.ActivationCountWeight / totalWeight);
    }

    public Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context)
    {
        var compatibleSilos = context.GetCompatibleSilos(target);

        if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
        {
            return Task.FromResult(placementHint);
        }

        if (compatibleSilos.Length == 0)
        {
            throw new SiloUnavailableException($"Cannot place grain '{target.GrainIdentity}' because there are no compatible silos.");
        }

        if (compatibleSilos.Length == 1)
        {
            return Task.FromResult(compatibleSilos[0]);
        }

        if (_siloStatistics.IsEmpty)
        {
            return Task.FromResult(compatibleSilos[Random.Shared.Next(compatibleSilos.Length)]);
        }

        // 通常建议在栈上分配的内存不超过 1 KB，
        // 但 ValueTuple<int, ResourceStatistics> 的大小为 24 字节，将
        // 上限提高到 4 KB 后，可使用 stackalloc 容纳集群中最多约 4096 / 24 ≈ 170 个 Silo。
        (int Index, float Score, float? LocalSiloScore) pick;
        int compatibleSilosCount = compatibleSilos.Length;
        if (compatibleSilosCount * Unsafe.SizeOf<(int, ResourceStatistics)>() <= FourKiloByte)
        {
            pick = MakePick(stackalloc (int, ResourceStatistics)[compatibleSilosCount]);
        }
        else
        {
            var relevantSilos = ArrayPool<(int, ResourceStatistics)>.Shared.Rent(compatibleSilosCount);
            pick = MakePick(relevantSilos.AsSpan());
            ArrayPool<(int, ResourceStatistics)>.Shared.Return(relevantSilos);
        }

        var localSiloScore = pick.LocalSiloScore;
        if (!localSiloScore.HasValue || context.LocalSiloStatus != SiloStatus.Active || localSiloScore.Value - _localSiloPreferenceMargin > pick.Score)
        {
            var bestCandidate = compatibleSilos[pick.Index];
            return Task.FromResult(bestCandidate);
        }

        return _cachedLocalSilo;

        (int PickIndex, float PickScore, float? LocalSiloScore) MakePick(scoped Span<(int, ResourceStatistics)> relevantSilos)
        {
            // 获取所有兼容且未过载的 Silo。
            int relevantSilosCount = 0;
            float maxMaxAvailableMemory = 0;
            int maxActivationCount = 0;
            ResourceStatistics? localSiloStatistics = null;
            for (var i = 0; i < compatibleSilos.Length; ++i)
            {
                var silo = compatibleSilos[i];
                if (_siloStatistics.TryGetValue(silo, out var stats))
                {
                    if (!stats.IsOverloaded)
                    {
                        relevantSilos[relevantSilosCount++] = new(i, stats);
                    }

                    if (stats.MaxAvailableMemory > maxMaxAvailableMemory)
                    {
                        maxMaxAvailableMemory = stats.MaxAvailableMemory;
                    }

                    if (stats.ActivationCount > maxActivationCount)
                    {
                        maxActivationCount = stats.ActivationCount;
                    }

                    if (silo.Equals(_localSilo))
                    {
                        localSiloStatistics = stats;
                    }
                }
            }

            // 限制为已添加的候选项数量。
            relevantSilos = relevantSilos[0..relevantSilosCount];
            Debug.Assert(relevantSilos.Length == relevantSilosCount);

            // 从兼容 Silo 列表中选择 K 个 Silo，其中 K 等于 Silo 数量的平方根。
            // 例如，10 个 Silo 中选择 4 个作为候选。
            int candidateCount = (int)Math.Ceiling(Math.Sqrt(relevantSilosCount));
            ShufflePrefix(relevantSilos, candidateCount);
            var candidates = relevantSilos[0..candidateCount];

            (int Index, float Score) pick = (0, 1f);

            foreach (var (index, statistics) in candidates)
            {
                float score = CalculateScore(in statistics, maxMaxAvailableMemory, maxActivationCount);

                // 虽然可能性很小，但仍可能有多个 Silo 具有相同的评分，
                // 因此引入少量随机扰动，避免总是选中候选列表中的第一个。
                float scoreJitter = Random.Shared.NextSingle() / 100_000f;

                if (score + scoreJitter < pick.Score)
                {
                    pick = (index, score);
                }
            }

            float? localSiloScore = null;
            if (localSiloStatistics.HasValue && !localSiloStatistics.Value.IsOverloaded)
            {
                var localStats = localSiloStatistics.Value;
                localSiloScore = CalculateScore(in localStats, maxMaxAvailableMemory, maxActivationCount);
            }

            return (pick.Index, pick.Score, localSiloScore);
        }

        // 现代 Fisher-Yates 洗牌算法的一种变体，在完成前 prefixLength 个元素的洗牌步骤后即停止，
        // 因为只需要关注这些元素。
        // 参见：https://en.wikipedia.org/wiki/Fisher%E2%80%93Yates_shuffle
        static void ShufflePrefix(Span<(int SiloIndex, ResourceStatistics SiloStatistics)> values, int prefixLength)
        {
            Debug.Assert(prefixLength >= 0 && prefixLength <= values.Length);

            var max = values.Length;
            for (var i = 0; i < prefixLength; i++)
            {
                var chosen = Random.Shared.Next(i, max);
                if (chosen != i)
                {
                    (values[chosen], values[i]) = (values[i], values[chosen]);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CalculateScore(ref readonly ResourceStatistics stats, float maxMaxAvailableMemory, int maxActivationCount)
    {
        float normalizedCpuUsage = stats.CpuUsage / 100f;
        Debug.Assert(normalizedCpuUsage >= 0f && normalizedCpuUsage <= 1.01f, "CPU usage should be normalized to [0, 1] range");
        float score = _weights.CpuUsageWeight * normalizedCpuUsage;

        if (stats.MaxAvailableMemory > 0)
        {
            float normalizedMemoryUsage = stats.NormalizedMemoryUsage;
            Debug.Assert(normalizedMemoryUsage >= 0f && normalizedMemoryUsage <= 1.01f, "Memory usage should be normalized to [0, 1] range");
            float normalizedAvailableMemory = stats.NormalizedAvailableMemory;
            Debug.Assert(normalizedAvailableMemory >= 0f && normalizedAvailableMemory <= 1.01f, "Available memory should be normalized to [0, 1] range");
            float normalizedMaxAvailableMemory = stats.MaxAvailableMemory / maxMaxAvailableMemory;
            Debug.Assert(normalizedMaxAvailableMemory >= 0f && normalizedMaxAvailableMemory <= 1.01f, "Max available memory should be normalized to [0, 1] range");

            score += _weights.MemoryUsageWeight * normalizedMemoryUsage +
                     _weights.AvailableMemoryWeight * (1 - normalizedAvailableMemory) +
                     _weights.MaxAvailableMemoryWeight * (1 - normalizedMaxAvailableMemory);
        }

        var normalizedActivationCount = stats.ActivationCount / (float)maxActivationCount;
        Debug.Assert(normalizedActivationCount >= 0f && normalizedActivationCount <= 1.01f, "Activation count should be normalized to [0, 1] range");
        score += _weights.ActivationCountWeight * normalizedActivationCount;

        Debug.Assert(score >= 0f && score <= 1.01f, "Score should be normalized to [0, 1] range");

        return score;
    }

    public void RemoveSilo(SiloAddress address)
         => _siloStatistics.TryRemove(address, out _);

    public void SiloStatisticsChangeNotification(SiloAddress address, SiloRuntimeStatistics statistics)
        => _siloStatistics.AddOrUpdate(
            key: address,
            factoryArgument: statistics,
            addValueFactory: static (_, statistics) => ResourceStatistics.FromRuntime(statistics),
            updateValueFactory: static (_, _, statistics) => ResourceStatistics.FromRuntime(statistics));

    private record NormalizedWeights(float CpuUsageWeight, float MemoryUsageWeight, float AvailableMemoryWeight, float MaxAvailableMemoryWeight, float ActivationCountWeight);
    private readonly record struct ResourceStatistics(bool IsOverloaded, float CpuUsage, float NormalizedMemoryUsage, float NormalizedAvailableMemory, float MaxAvailableMemory, int ActivationCount)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ResourceStatistics FromRuntime(SiloRuntimeStatistics statistics)
            => new(
                IsOverloaded: statistics.IsOverloaded,
                CpuUsage: statistics.EnvironmentStatistics.FilteredCpuUsagePercentage,
                NormalizedMemoryUsage: statistics.EnvironmentStatistics.NormalizedFilteredMemoryUsage,
                NormalizedAvailableMemory: statistics.EnvironmentStatistics.NormalizedFilteredAvailableMemory,
                MaxAvailableMemory: statistics.EnvironmentStatistics.MaximumAvailableMemoryBytes,
                ActivationCount: statistics.ActivationCount);
    }
}
