using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Runtime.Placement
{

    internal class ActivationCountPlacementDirector : RandomPlacementDirector, ISiloStatisticsChangeListener, IPlacementDirector
    {
        private class CachedLocalStat
        {
            private int _activationCount;

            internal CachedLocalStat(SiloRuntimeStatistics siloStats) => SiloStats = siloStats;

            public SiloRuntimeStatistics SiloStats { get; }
            public int ActivationCount => _activationCount;
            public void IncrementActivationCount(int delta) => Interlocked.Add(ref _activationCount, delta);
        }

        // 音轨在统计间隔间距之间在此 silo 上创建了激活 。
        private readonly ConcurrentDictionary<SiloAddress, CachedLocalStat> _localCache = new();
        private readonly SiloAddress _localAddress;
        private readonly int _chooseHowMany;

        public ActivationCountPlacementDirector(
            ILocalSiloDetails localSiloDetails,
            DeploymentLoadPublisher deploymentLoadPublisher,
            IOptions<ActivationCountBasedPlacementOptions> options)
        {
            _localAddress = localSiloDetails.SiloAddress;
            _chooseHowMany = options.Value.ChooseOutOf;
            if (_chooseHowMany <= 0) throw new ArgumentException($"{nameof(ActivationCountBasedPlacementOptions)}.{nameof(ActivationCountBasedPlacementOptions.ChooseOutOf)} is {_chooseHowMany}. It must be greater than zero.");
            deploymentLoadPublisher?.SubscribeToStatisticsChangeEvents(this);
        }

        private SiloAddress SelectSiloPowerOfK(SiloAddress[] silos)
        {
            if (silos.Length == 0)
            {
                throw new SiloUnavailableException("Unable to select a candidate because there are no compatible silos.");
            }

            // 排除超载和不兼容 silos
            var relevantSilos = new List<KeyValuePair<SiloAddress, CachedLocalStat>>();
            var totalSilos = _localCache.Count;
            var compatibleSilosWithoutStats = 0;
            SiloAddress? sampledCompatibleSiloWithoutStats = default;
            foreach (var silo in silos)
            {
                if (!_localCache.TryGetValue(silo, out var localSiloStat))
                {
                    compatibleSilosWithoutStats++;
                    if (compatibleSilosWithoutStats == 1 || Random.Shared.Next(compatibleSilosWithoutStats) == 0)
                    {
                        sampledCompatibleSiloWithoutStats = silo;
                    }

                    continue;
                }

                if (localSiloStat.SiloStats.IsOverloaded) continue;

                relevantSilos.Add(new(silo, localSiloStat));
            }

            if (relevantSilos.Count > 0)
            {
                int chooseFrom = Math.Min(relevantSilos.Count, _chooseHowMany);
                var chooseFromThoseSilos = new List<KeyValuePair<SiloAddress, CachedLocalStat>>(chooseFrom);
                while (chooseFromThoseSilos.Count < chooseFrom)
                {
                    int index = Random.Shared.Next(relevantSilos.Count);
                    var pickedSilo = relevantSilos[index];
                    relevantSilos.RemoveAt(index);
                    chooseFromThoseSilos.Add(pickedSilo);
                }

                KeyValuePair<SiloAddress, CachedLocalStat> minLoadedSilo = default;
                var minLoad = int.MaxValue;
                foreach (var s in chooseFromThoseSilos)
                {
                    var load = s.Value.ActivationCount + s.Value.SiloStats.RecentlyUsedActivationCount;
                    if (load < minLoad)
                    {
                        minLoadedSilo = s;
                        minLoad = load;
                    }
                }

                // 增加安置,按 silos 而不是按1编号计算。
                // 这是我们的伎俩 来更平衡地安置, 考虑可能
                // 复数 silos 位于同一时间,在更新数据之前的多个 silo 位置。
                minLoadedSilo.Value.IncrementActivationCount(totalSilos);

                return minLoadedSilo.Key;
            }

            // 有些兼容的 silos 可能尚未公布统计数据。
            if (compatibleSilosWithoutStats > 0)
            {
                return sampledCompatibleSiloWithoutStats!;
            }

            // 所有兼容的 silos 都公布了统计,并超载。
            var allSiloStats = _localCache.ToList();
            throw new SiloUnavailableException(
                $"Unable to select a candidate from {silos.Length} compatible silos (all are overloaded). All silo stats: {Utils.EnumerableToString(allSiloStats, kvp => $"SiloAddress = {kvp.Key} -> IsOverloaded = {kvp.Value.SiloStats.IsOverloaded}, ActivationCount = {kvp.Value.ActivationCount}, RecentlyUsedActivationCount = {kvp.Value.SiloStats.RecentlyUsedActivationCount}")}");
        }

        public override Task<SiloAddress> OnAddActivation(PlacementStrategy strategy, PlacementTarget target, IPlacementContext context) => Task.FromResult(OnAddActivationInternal(target, context));

        private SiloAddress OnAddActivationInternal(PlacementTarget target, IPlacementContext context)
        {
            var compatibleSilos = context.GetCompatibleSilos(target);

            // 如果指定了有效的放置提示, 请使用它 。
            if (IPlacementDirector.GetPlacementHint(target.RequestContextData, compatibleSilos) is { } placementHint)
            {
                return placementHint;
            }

            // 如果缓存不包含人口, 只有当 silo 兼容时才在本地放置。
            if (_localCache.IsEmpty)
            {
                if (compatibleSilos.Contains(_localAddress))
                {
                    return _localAddress;
                }

                return SelectSiloPowerOfK(compatibleSilos);
            }

            return SelectSiloPowerOfK(compatibleSilos);
        }

        public void SiloStatisticsChangeNotification(SiloAddress updatedSilo, SiloRuntimeStatistics newSiloStats)
        {
            // 只需创建一个新的空 CachedLocalStat ,然后扔掉旧的。
            _localCache[updatedSilo] = new(newSiloStats);
        }

        public void RemoveSilo(SiloAddress removedSilo)
        {
            _localCache.TryRemove(removedSilo, out _);
        }
    }
}
