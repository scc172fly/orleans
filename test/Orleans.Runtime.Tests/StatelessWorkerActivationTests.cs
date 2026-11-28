using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.General;

/// <summary>
/// 测试无状态工作者 Grain 的激活及扩缩容行为。
/// 
/// 无状态工作者 Grain 是一种标记了 [StatelessWorker] 特性的特殊 Grain，它具有以下特点：
/// - 在同一 Silo 上可具有多个激活实例（最多达到 MaxLocalWorkers 限制）
/// - 根据负载自动扩缩容
/// - 不在调用之间保持状态
/// - 非常适合可并行化的 CPU 密集型或 I/O 受限操作
/// 
/// 这些测试验证：
/// - 低负载时的单一激活行为
/// - 并发负载下自动扩容至 MaxLocalWorkers
/// - 激活实例停用时正确清理
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Runtime")]
public class StatelessWorkerActivationTests : IClassFixture<StatelessWorkerActivationTests.Fixture>
{
    public class Fixture : BaseTestClusterFixture
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 1;
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        }

        private class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                // 测试 Grain 用于跟踪激活数量的共享状态服务
                hostBuilder.Services.AddSingleton<StatelessWorkerScalingGrainSharedState>();
            }
        }
    }

    private readonly Fixture _fixture;

    public StatelessWorkerActivationTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// 验证无状态工作者 Grain 在请求顺序执行（没有并发负载）时
    /// 仅保留一个激活实例。
    /// 这说明 Orleans 不会在不需要时
    /// 创建多个激活实例。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("StatelessWorker")]
    public async Task SingleWorkerInvocationUnderLoad()
    {
        var workerGrain = _fixture.GrainFactory.GetGrain<IStatelessWorkerScalingGrain>(0);
        var managementGrain = _fixture.GrainFactory.GetGrain<IManagementGrain>(0);
        var grainReference = (GrainReference)workerGrain;

        for (var i = 0; i < 100; i++)
        {
            await workerGrain.GetActivationCount();
            await Until(
                async () => 1 == await managementGrain.GetGrainActivationCount(grainReference),
                "the activation count to remain at 1",
                5_000);
        }
    }

    /// <summary>
    /// 测试并发负载下无状态工作者激活实例的自动扩容。
    /// 
    /// 流程：
    /// 1. 从一个激活实例开始
    /// 2. 创建会阻塞的并发请求（使用 Wait()）
    /// 3. Orleans 检测到激活实例被阻塞并创建新的实例
    /// 4. 持续扩容直至 MaxLocalWorkers（本测试为 4）
    /// 
    /// 这说明 Orleans 可以根据实际负载（而非仅根据请求数量）
    /// 自动扩容无状态工作者。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("StatelessWorker")]
    public async Task MultipleWorkerInvocationUnderLoad()
    {
        const int MaxLocalWorkers = 4;  // 每个 Silo 的最大激活数量
        var waiters = new List<Task>();  // 跟踪阻塞调用
        var worker = _fixture.GrainFactory.GetGrain<IStatelessWorkerScalingGrain>(1);
        var primary = Assert.IsType<InProcessSiloHandle>(_fixture.HostedCluster.Primary);
        var sharedState = primary.SiloHost.Services.GetRequiredService<StatelessWorkerScalingGrainSharedState>();
        var grainId = ((GrainReference)worker).GrainId;

        try
        {
            // 最初仅存在一个激活实例
            var activationCount = await worker.GetActivationCount();
            Assert.Equal(1, activationCount);

            // 第一个阻塞调用触发创建第二个激活实例
            waiters.Add(worker.Wait());
            await Until(
                () => Task.FromResult(1 == sharedState.WaitingActivations[grainId]),
                "the first activation to enter Wait");
            activationCount = await worker.GetActivationCount();
            Assert.Equal(2, activationCount);

            waiters.Add(worker.Wait());
            await Until(
                () => Task.FromResult(2 == sharedState.WaitingActivations[grainId]),
                "the second activation to enter Wait");
            activationCount = await worker.GetActivationCount();
            Assert.Equal(3, activationCount);

            waiters.Add(worker.Wait());
            await Until(
                () => Task.FromResult(3 == sharedState.WaitingActivations[grainId]),
                "the third activation to enter Wait");
            activationCount = await worker.GetActivationCount();
            Assert.Equal(4, activationCount);

            var waitingCount = sharedState.WaitingActivations[grainId];
            Assert.Equal(3, waitingCount);

            for (var i = 0; i < MaxLocalWorkers; i++)
            {
                waiters.Add(worker.Wait());
            }

            await Until(
                () => Task.FromResult(MaxLocalWorkers == sharedState.WaitingActivations[grainId]),
                $"the waiting count to reach {MaxLocalWorkers}");
            activationCount = sharedState.ActivationCounts[grainId];
            Assert.Equal(MaxLocalWorkers, activationCount);
            waitingCount = sharedState.WaitingActivations[grainId];
            Assert.Equal(MaxLocalWorkers, waitingCount);
        }
        finally
        {
            if (waiters.Count > 0)
            {
                sharedState.Semaphore.Release(waiters.Count);
            }
        }

        await Task.WhenAll(waiters);
    }

    /// <summary>
    /// 测试无状态工作者激活实例停用时，是否从目录（Catalog）中
    /// 正确清理。这对于以下事项很重要：
    /// - 防止内存泄漏
    /// - 确保激活计数准确
    /// - 验证 Grain 目录是否正确跟踪无状态工作者
    /// 
    /// 使用管理 Grain 强制回收激活实例并验证清理结果。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("StatelessWorker")]
    public async Task CatalogCleanupOnDeactivation()
    {
        var workerGrain = _fixture.GrainFactory.GetGrain<IStatelessWorkerGrain>(0);
        var mgmt = _fixture.GrainFactory.GetGrain<IManagementGrain>(0);

        var numActivations = await mgmt.GetGrainActivationCount(
            (GrainReference)workerGrain,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, numActivations);

        // 使用空操作调用激活 Grain
        await workerGrain.DummyCall();

        numActivations = await mgmt.GetGrainActivationCount(
            (GrainReference)workerGrain,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, numActivations);

        // 强制立即执行激活实例回收以触发停用
        // TimeSpan.Zero 表示立即回收所有空闲激活实例
        await mgmt.ForceActivationCollection(TimeSpan.Zero, TestContext.Current.CancellationToken);

        // 无状态工作者 Grain 的激活计数应重新变为 0
        await Until(
            async () => await mgmt.GetGrainActivationCount(
                (GrainReference)workerGrain,
                TestContext.Current.CancellationToken) == 0,
            "the activation count to reach 0",
            5_000
        );
    }

    /// <summary>
    /// 等待异步条件变为 true 的辅助方法。
    /// 用于处理激活实例创建及销毁过程中的最终一致性。
    /// </summary>
    private static async Task Until(Func<Task<bool>> condition, string description, int maxTimeout = 40_000)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(maxTimeout);

        try
        {
            while (!await condition().WaitAsync(timeout.Token))
            {
                await Task.Delay(10, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail($"Timed out after {maxTimeout}ms waiting for {description}.");
        }
    }
}
