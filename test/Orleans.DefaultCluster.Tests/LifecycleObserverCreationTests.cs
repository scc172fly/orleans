using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.TestingHost;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 在参与生命周期期间建立 grain 观察员的测试。
    /// 由此证实 grain 观察员可在 silo 生命周期阶段设立,
    /// 先前因生命周期事件在 SystemTarget上执行而失败的
    /// (grain 预防、预防、预防、 CreateObjectReference 工作时
    /// </summary>
    [TestCategory("BVT"), TestCategory("Lifecycle"), TestCategory("Observer")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class LifecycleObserverCreationTests
    {
        /// <summary>
        /// 测试在参与生命周期期间能够建立 grain 观察员。
        /// 这样就能验证对 CreateObjectReference 将失败问题的解决
        /// 使用“ 无法从 grain中创建本地对象引用,” 时在
        /// 生命周期活动,因为这些活动以前是安排在 SystemTarget上。
        /// </summary>
        [Fact]
        public async Task LifecycleParticipant_CanCreateGrainObserver()
        {
            var observerCreated = false;
            var grainCalled = false;

            // 创建一个由生命周期参与者组成的测试组,以创建观察员
            var builder = new InProcessTestClusterBuilder();
            builder.ConfigureSilo((options, siloBuilder) =>
            {
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(serviceProvider =>
                        new TestLifecycleParticipant(
                            serviceProvider.GetRequiredService<IGrainFactory>(),
                            () => observerCreated = true,
                            () => grainCalled = true));
                });
            });

            var cluster = builder.Build();
            await cluster.DeployAsync(TestContext.Current.CancellationToken);

            try
            {
                // 核查观察员是在参与生命周期期间创建的
                Assert.True(observerCreated, "Observer should have been created during lifecycle participation");

                // 核实在参与生命周期期间成功发出 grain 呼叫
                Assert.True(grainCalled, "Grain should have been called during lifecycle participation");

                // 还在启动后通过调用 grain 来核实该组组是否开始运作
                var grain = cluster.Client!.GetGrain<ISimpleGrain>(42);
                await grain.SetA(100);
                var value = await grain.GetA();
                Assert.Equal(100, value);
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }

        /// <summary>
        /// 生命周期参与者在活动阶段创建了一名 grain 观察员。
        /// 这模拟了托管服务或生命周期参与者的发行情况
        /// 需要创建 grain 观察员在 silo 启动。
        /// </summary>
        private class TestLifecycleParticipant : ILifecycleParticipant<ISiloLifecycle>
        {
            private readonly IGrainFactory _grainFactory;
            private readonly Action _onObserverCreated;
            private readonly Action _onGrainCalled;

            public TestLifecycleParticipant(IGrainFactory grainFactory, Action onObserverCreated, Action onGrainCalled)
            {
                _grainFactory = grainFactory;
                _onObserverCreated = onObserverCreated;
                _onGrainCalled = onGrainCalled;
            }

            public void Participate(ISiloLifecycle lifecycle)
            {
                lifecycle.Subscribe(
                    nameof(TestLifecycleParticipant),
                    ServiceLifecycleStage.Active,
                    OnStart,
                    OnStop);
            }

            private async Task OnStart(CancellationToken ct)
            {
                // 这是关键测试 - 在参与生命周期期间创建 grain 观察员
                // 现在,生命周期事件通过 Task.Run 而不是在 SystemTarget进行,因此应该工作起来
                var observer = new TestObserver();
                var reference = _grainFactory.CreateObjectReference<ISimpleGrainObserver>(observer);

                // 测试成功创建了引用
                Assert.NotNull(reference);
                _onObserverCreated();

                // 测试我们能否在参与生命周期期间发出 grain 呼叫
                var grain = _grainFactory.GetGrain<ISimpleGrain>(123);
                await grain.SetA(50);
                var value = await grain.GetA();
                Assert.Equal(50, value);
                _onGrainCalled();

                // 清理
                _grainFactory.DeleteObjectReference<ISimpleGrainObserver>(reference);
            }

            private Task OnStop(CancellationToken ct)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 测试简单的观察器安装。
        /// </summary>
        private class TestObserver : ISimpleGrainObserver
        {
            public void StateChanged(int a, int b)
            {
                // 不允许试验
            }
        }
    }
}
