using Orleans.Configuration;
using Orleans.TestingHost;
using Orleans.Internal;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;
using Orleans.Runtime;
using UnitTests.Grains;

namespace UnitTests.StuckGrainTests
{
    /// <summary>
    /// PersistenceTest简 简 述
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class StuckGrainTests : OrleansTestingBase, IClassFixture<StuckGrainTests.Fixture>
    {
        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<SiloHostConfigurator>();
            }

            private class SiloHostConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder.Configure<GrainCollectionOptions>(options =>
                    {
                        options.CollectionAge = TimeSpan.FromSeconds(2);
                        options.CollectionQuantum = TimeSpan.FromSeconds(1);

                        options.ActivationTimeout = TimeSpan.FromSeconds(2);
                        options.DeactivationTimeout = TimeSpan.FromSeconds(2);
                    });

                    hostBuilder.Configure<SiloMessagingOptions>(options =>
                    {
                        options.MaxRequestProcessingTime = TimeSpan.FromSeconds(3);
                    });
                }
            }
        }

        public StuckGrainTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact, TestCategory("Functional"), TestCategory("ActivationCollection")]
        public async Task StuckGrainTest_Basic()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var id = Guid.NewGuid();
            var stuckGrain = this.fixture.GrainFactory.GetGrain<IStuckGrain>(id);
            var task = stuckGrain.RunForever();

            // 是否超时
            await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken));

            var cleaner = this.fixture.GrainFactory.GetGrain<IStuckCleanGrain>(id);
            await cleaner.Release(id);

            // 现在就应该完成
            await task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

            // 等待激活收藏
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);

            Assert.False(await cleaner.IsActivated(id), "Grain activation is supposed be garbage collected, but it is still running.");
        }

        [Fact, TestCategory("Functional"), TestCategory("ActivationCollection")]
        public async Task StuckGrainTest_StuckDetectionAndForward()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var id = Guid.NewGuid();
            var stuckGrain = this.fixture.GrainFactory.GetGrain<IStuckGrain>(id);
            var task = stuckGrain.RunForever();

            // 是否超时
            await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken));

            var calls = new Task[3];
            for (var i = 0; i < calls.Length; i++)
            {
                var call = stuckGrain.NonBlockingCall();
                calls[i] = call;
                await Task.WhenAny(call, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
            }

            // 等待,这样第一件任务就会完成 DefaultCollectionAge 超时
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            // 这件事没有问题
            await stuckGrain.NonBlockingCall();

            // 否则所有被困的电话 都应该被传送到新的启动程序
            await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("ActivationCollection")]
        public async Task StuckGrainTest_DeactivationForwardsCallsAfterOnDeactivateAsyncCompletes()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var id = Guid.NewGuid();
            var stuckGrain = this.fixture.GrainFactory.GetGrain<IStuckGrain>(id);
            await stuckGrain.BlockingDeactivation();

            await StuckGrain.WaitForDeactivationStart(stuckGrain.GetGrainId());

            var calls = new Task[3];
            for (var i = 0; i < calls.Length; i++)
            {
                calls[i] = stuckGrain.NonBlockingCall();
            }

            // 准备状态保证解除回击的释放任务得到登记。
            Assert.True(StuckGrain.Release(id));
            await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            // 这件事没有问题
            await stuckGrain.NonBlockingCall();

            Assert.Equal(4, await stuckGrain.GetNonBlockingCallCounter(cancellationToken));
        }

        [Fact, TestCategory("Functional"), TestCategory("ActivationCollection")]
        public async Task StuckGrainTest_StuckDetectionOnActivation()
        {
            var id = Guid.NewGuid();
            var stuckGrain = this.fixture.GrainFactory.GetGrain<IStuckGrain>(id);

            // 取消已传递给 OnActivateAsync 被取消,启动失败一次 OnActivateAsync 返回。
            RequestContext.Set("block_activation_seconds", 30);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stuckGrain.NonBlockingCall());

            // 检查看看它是否尝试激活。
            RequestContext.Clear();
            var unstuckGrain = this.fixture.GrainFactory.GetGrain<IStuckGrain>(Guid.NewGuid());
            await unstuckGrain.NonBlockingCall();

            var activationAttempted = await unstuckGrain.DidActivationTryToStart(stuckGrain.GetGrainId());
            Assert.True(activationAttempted);

            // 现在激活没有被屏蔽( 我们清除了请求的上下文值, 要求它屏蔽) , 让我们检查一下我们先前卡住的 grain 是否有效 。
            await stuckGrain.NonBlockingCall();
        }
    }
}
