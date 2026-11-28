using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Configuration;
using Orleans.Placement;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using Orleans.Runtime.TestHooks;
using Orleans.Statistics;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.Grains
{
    internal abstract class PlacementTestGrainBase : Grain
    {
        private readonly string _id = Guid.NewGuid().ToString();

        private readonly OverloadDetector overloadDetector;

        private readonly TestHooksEnvironmentStatisticsProvider environmentStatistics;
        private readonly IGrainContext _grainContext;
        private readonly LoadSheddingOptions loadSheddingOptions;

        public PlacementTestGrainBase(
            OverloadDetector overloadDetector,
            TestHooksEnvironmentStatisticsProvider hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            IGrainContext grainContext)
        {
            _grainContext = grainContext;
            this.overloadDetector = overloadDetector;
            this.environmentStatistics = hostEnvironmentStatistics;
            this.loadSheddingOptions = loadSheddingOptions.Value;
        }

        public Task<IPEndPoint> GetEndpoint()
        {
            return Task.FromResult(_grainContext.Address.SiloAddress!.Endpoint);
        }

        public Task<string> GetRuntimeInstanceId()
        {
            return Task.FromResult(RuntimeIdentity);
        }

        public Task<string> GetActivationId()
        {
            return Task.FromResult(_id);
        }

        public Task Nop()
        {
            return Task.CompletedTask;
        }

        public Task StartLocalGrains(List<Guid> keys)
        {
            // 调用 Grain 引用的 Nop()，确保在 Promise 完成前已将相应 Grain 实例化。
            var grains = keys.Select(i => GrainFactory.GetGrain<IStatelessWorkerPlacementTestGrain>(i));
            var promises = grains.Select(g => g.Nop());
            return Task.WhenAll(promises);
        }

        public async Task<Guid> StartPreferLocalGrain(Guid key)
        {
            // 调用 Grain 引用的 Nop()，确保在 Promise 完成前已将相应 Grain 实例化。
            await GrainFactory.GetGrain<IPreferLocalPlacementTestGrain>(key).Nop();
            return key;
        }

        private static IEnumerable<Task<IPEndPoint>> SampleLocalGrainEndpoint(IStatelessWorkerPlacementTestGrain grain, int sampleSize)
        {
            for (var i = 0; i < sampleSize; ++i)
                yield return grain.GetEndpoint();
        }

        public async Task<List<IPEndPoint>> SampleLocalGrainEndpoint(Guid key, int sampleSize)
        {
            var grain = GrainFactory.GetGrain<IStatelessWorkerPlacementTestGrain>(key);
            var p = await Task.WhenAll(SampleLocalGrainEndpoint(grain, sampleSize));
            return p.ToList();
        }

        private static async Task PropagateStatisticsToCluster(IGrainFactory grainFactory)
        {
            // 强制将已锁存的统计数据传播到整个集群。
            var managementGrain = grainFactory.GetGrain<IManagementGrain>(0);
            var hosts = await managementGrain.GetHosts(true);
            var keys = hosts.Select(kvp => kvp.Key).ToArray();
            await managementGrain.ForceRuntimeStatisticsCollection(keys);
        }

        public Task EnableOverloadDetection(bool enabled)
        {
            this.overloadDetector.Enabled = enabled;
            return Task.CompletedTask;
        }

        public Task LatchOverloaded()
        {
            var stats = environmentStatistics.GetEnvironmentStatistics();
            environmentStatistics.LatchHardwareStatistics(new EnvironmentStatistics(
                cpuUsagePercentage: loadSheddingOptions.CpuThreshold + 1,
                rawCpuUsagePercentage: loadSheddingOptions.CpuThreshold + 1,
                memoryUsageBytes: stats.FilteredMemoryUsageBytes,
                rawMemoryUsageBytes: stats.RawMemoryUsageBytes,
                availableMemoryBytes: stats.FilteredAvailableMemoryBytes,
                rawAvailableMemoryBytes: stats.RawAvailableMemoryBytes,
                maximumAvailableMemoryBytes: stats.MaximumAvailableMemoryBytes));
            return PropagateStatisticsToCluster(GrainFactory);
        }

        public Task UnlatchOverloaded()
        {
            var stats = environmentStatistics.GetEnvironmentStatistics();
            environmentStatistics.LatchHardwareStatistics(new(
                cpuUsagePercentage: 0,
                rawCpuUsagePercentage: 0,
                memoryUsageBytes: stats.FilteredMemoryUsageBytes,
                rawMemoryUsageBytes: stats.RawMemoryUsageBytes,
                availableMemoryBytes: stats.FilteredAvailableMemoryBytes,
                rawAvailableMemoryBytes: stats.RawAvailableMemoryBytes,
                maximumAvailableMemoryBytes: stats.MaximumAvailableMemoryBytes));
            return PropagateStatisticsToCluster(GrainFactory);
        }

        public Task LatchCpuUsage(float value)
        {
            var stats = environmentStatistics.GetEnvironmentStatistics();
            environmentStatistics.LatchHardwareStatistics(new(
                cpuUsagePercentage: value,
                rawCpuUsagePercentage: value,
                memoryUsageBytes: stats.FilteredMemoryUsageBytes,
                rawMemoryUsageBytes: stats.RawMemoryUsageBytes,
                availableMemoryBytes: stats.FilteredAvailableMemoryBytes,
                rawAvailableMemoryBytes: stats.RawAvailableMemoryBytes,
                maximumAvailableMemoryBytes: stats.MaximumAvailableMemoryBytes));
            return PropagateStatisticsToCluster(GrainFactory);
        }

        public Task UnlatchCpuUsage()
        {
            var stats = environmentStatistics.GetEnvironmentStatistics();
            environmentStatistics.LatchHardwareStatistics(new(
                cpuUsagePercentage: 0,
                rawCpuUsagePercentage: 0,
                memoryUsageBytes: stats.FilteredMemoryUsageBytes,
                rawMemoryUsageBytes: stats.RawMemoryUsageBytes,
                availableMemoryBytes: stats.FilteredAvailableMemoryBytes,
                rawAvailableMemoryBytes: stats.RawAvailableMemoryBytes,
                maximumAvailableMemoryBytes: stats.MaximumAvailableMemoryBytes));
            return PropagateStatisticsToCluster(GrainFactory);
        }

        public Task<SiloAddress> GetLocation()
        {
            return Task.FromResult(_grainContext.Address.SiloAddress!);
        }
    }

    [RandomPlacement]
    internal class RandomPlacementTestGrain : PlacementTestGrainBase, IRandomPlacementTestGrain
    {
        public RandomPlacementTestGrain(
            OverloadDetector overloadDetector,
            TestHooksEnvironmentStatisticsProvider hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            IGrainContext grainContext)
            : base(overloadDetector, hostEnvironmentStatistics, loadSheddingOptions, grainContext)
        {
        }
    }

    [PreferLocalPlacement]
    internal class PreferLocalPlacementTestGrain : PlacementTestGrainBase, IPreferLocalPlacementTestGrain
    {
        public PreferLocalPlacementTestGrain(
            OverloadDetector overloadDetector,
            TestHooksEnvironmentStatisticsProvider hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            IGrainContext grainContext)
            : base(overloadDetector, hostEnvironmentStatistics, loadSheddingOptions, grainContext)
        {
        }
    }

    [StatelessWorker(1)]
    internal class StatelessWorkerPlacementTestGrain : PlacementTestGrainBase, IStatelessWorkerPlacementTestGrain
    {
        public StatelessWorkerPlacementTestGrain(
            OverloadDetector overloadDetector,
            TestHooksEnvironmentStatisticsProvider hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            IGrainContext grainContext)
            : base(overloadDetector, hostEnvironmentStatistics, loadSheddingOptions, grainContext)
        {
        }

        public ValueTask<int> GetWorkerLimit()
        {
            var placementStrategy = GrainContext.GetComponent<PlacementStrategy>();
            if (placementStrategy is not StatelessWorkerPlacement statelessWorkerPlacement)
            {
                throw new InvalidOperationException($"Unexpected placement strategy: {placementStrategy}");
            }

            return new(statelessWorkerPlacement.MaxLocal);
        }
    }

    [StatelessWorker(2)]
    internal class OtherStatelessWorkerPlacementTestGrain : PlacementTestGrainBase, IOtherStatelessWorkerPlacementTestGrain
    {
        public OtherStatelessWorkerPlacementTestGrain(
            OverloadDetector overloadDetector,
            TestHooksEnvironmentStatisticsProvider hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            IGrainContext grainContext)
            : base(overloadDetector, hostEnvironmentStatistics, loadSheddingOptions, grainContext)
        {
        }

        public ValueTask<int> GetWorkerLimit()
        {
            var placementStrategy = GrainContext.GetComponent<PlacementStrategy>();
            if (placementStrategy is not StatelessWorkerPlacement statelessWorkerPlacement)
            {
                throw new InvalidOperationException($"Unexpected placement strategy: {placementStrategy}");
            }

            return new(statelessWorkerPlacement.MaxLocal);
        }
    }

    [ActivationCountBasedPlacement]
    internal class ActivationCountBasedPlacementTestGrain : PlacementTestGrainBase, IActivationCountBasedPlacementTestGrain
    {
        public ActivationCountBasedPlacementTestGrain(
            OverloadDetector overloadDetector,
            TestHooksEnvironmentStatisticsProvider hostEnvironmentStatistics,
            IOptions<LoadSheddingOptions> loadSheddingOptions,
            IGrainContext grainContext)
            : base(overloadDetector, hostEnvironmentStatistics, loadSheddingOptions, grainContext)
        {
        }
    }

    internal class DefaultPlacementGrain : Grain, IDefaultPlacementGrain
    {
        public Task<PlacementStrategy> GetDefaultPlacement()
        {
            var defaultStrategy = this.ServiceProvider.GetRequiredService<PlacementStrategy>();
            return Task.FromResult(defaultStrategy);
        }
    }

    //----------------------------------------------------------//
    // LocalContent Grain 场景的 Grain，其中引导提供程序会在每个 Silo 上激活该 Grain。

    [PreferLocalPlacement]
    public class LocalContentGrain : Grain, ILocalContentGrain
    {
        private readonly ILogger logger;
        private object cachedContent = null!;
        internal static ILocalContentGrain InstanceIdForThisSilo = null!;

        public LocalContentGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");
            DelayDeactivation(TimeSpan.MaxValue);   // 确保此激活实例不会被回收。
            cachedContent = RuntimeIdentity;        // 将自身的 Silo 标识作为本地缓存内容存入此 Grain。
            InstanceIdForThisSilo = this.AsReference<ILocalContentGrain>();
            return Task.FromResult(0);
        }

        public Task Init()
        {
            logger.LogInformation("Init LocalContentGrain on silo {RuntimeIdentity}", RuntimeIdentity);
            return Task.FromResult(0);
        }

        public Task<object> GetContent()
        {
            return Task.FromResult(cachedContent);
        }
    }

    public class TestContentGrain : Grain, ITestContentGrain
    {
        private readonly ILogger logger;

        public TestContentGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");
            return base.OnActivateAsync(cancellationToken);
        }

        public Task<string> GetRuntimeInstanceId()
        {
            logger.LogInformation("GetRuntimeInstanceId");
            return Task.FromResult(RuntimeIdentity);
        }

        public async Task<object> FetchContentFromLocalGrain()
        {
            logger.LogInformation("FetchContentFromLocalGrain");
            var localContentGrain = LocalContentGrain.InstanceIdForThisSilo;
            if (localContentGrain == null)
            {
                throw new Exception("LocalContentGrain was not correctly initialized during silo startup!");
            }
            object content = await localContentGrain.GetContent();
            logger.LogInformation("Received content = {Content}", content);
            return content;
        }
    }

}
