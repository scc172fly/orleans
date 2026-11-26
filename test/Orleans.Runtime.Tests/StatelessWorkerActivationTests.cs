using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.General;

/// <summary>
/// 无国籍工人的 grain 激活和调整行为测试。
/// 
/// 无国籍工人 grains 是一种特殊类型的 grain ,其属性[StatelessWorker]:
/// - 在同一 silo 上(不超过 MaxLocalWorkers 限值)可有多个引爆装置
/// - 根据负载自动缩放
/// - 别保持通话状态
/// - 适合可平行进行CPU密集或I/O-受限制的运行
/// 
/// 这些测试核实:
/// - 负载低时单一激活行为
/// - 自动在同时装载下提升至 MaxLocalWorkers
/// - 解除引爆装置时适当清理
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
                // 用于跟踪激活计数的测试 grains 所用的共享国家服务
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
    /// 证明无国籍工人 grain 保持单一的激活
    /// 当请求相继( 没有同时装入) 时进行请求 。
    /// 这证明 Orleans 不会不必要地创造多个
    /// 不需要时启动
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
    /// 测试在同时负荷下自动提升无国籍工人的活化。
    /// 
    /// 进程:
    /// 1. 以单一启动开始
    /// 2. 创建同时提出的(使用Wait ())块要求
    /// 3. Orleans 检测阻断的启动并创建新的
    /// 4. 提升继续到 MaxLocalWorkers [本测试中4]
    /// 
    /// 这表明 Orleans有能力自动规模化无国籍
    /// 工人根据实际负荷,而不仅仅是要求计数。
    /// </summary>
    [Fact, TestCategory("BVT"), TestCategory("StatelessWorker")]
    public async Task MultipleWorkerInvocationUnderLoad()
    {
        const int MaxLocalWorkers = 4;  // 最大激活量 silo
        var waiters = new List<Task>();  // 音轨阻断呼叫
        var worker = _fixture.GrainFactory.GetGrain<IStatelessWorkerScalingGrain>(1);
        var primary = Assert.IsType<InProcessSiloHandle>(_fixture.HostedCluster.Primary);
        var sharedState = primary.SiloHost.Services.GetRequiredService<StatelessWorkerScalingGrainSharedState>();
        var grainId = ((GrainReference)worker).GrainId;

        try
        {
            // 最初,只有一个激活装置
            var activationCount = await worker.GetActivationCount();
            Assert.Equal(1, activationCount);

            // 第一次屏蔽呼叫触发了第二次启动
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
    /// 测试无国籍工人的激活是否从目录中适当清理
    /// 当被禁用时。 这对以下各方面很重要:
    /// - 防止内存泄漏
    /// - 确保准确的启动计数
    /// - 核查 grain 目录,适当跟踪无国籍工人
    /// 
    /// 强制激活收集与核查清理的用途管理 grain 。
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

        // 以一个假电话激活 grain
        await workerGrain.DummyCall();

        numActivations = await mgmt.GetGrainActivationCount(
            (GrainReference)workerGrain,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, numActivations);

        // 强制即时启动集合,以触发解除功能
        // TimeSpan.Zero 是指立即收集所有闲置激活
        await mgmt.ForceActivationCollection(TimeSpan.Zero, TestContext.Current.CancellationToken);

        // 无国籍工人的启动计数 grain 应再次成为 0
        await Until(
            async () => await mgmt.GetGrainActivationCount(
                (GrainReference)workerGrain,
                TestContext.Current.CancellationToken) == 0,
            "the activation count to reach 0",
            5_000
        );
    }

    /// <summary>
    /// 帮助器方法, 等待一个同步状态变成真实 。
    /// 用于处理引爆装置的产生/销毁方面最终的一致性。
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
