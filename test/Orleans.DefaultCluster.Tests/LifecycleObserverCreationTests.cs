using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.TestingHost;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试参与生命周期时创建 Grain 观察者。
    /// 这验证了可以在 Silo 生命周期阶段创建 Grain 观察者，
    /// 此前由于生命周期事件在 SystemTarget
    /// （Grain 上下文）上执行，导致 CreateObjectReference 无法工作。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Lifecycle"), TestCategory("Observer")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class LifecycleObserverCreationTests
    {
        /// <summary>
        /// 测试能否在参与生命周期时创建 Grain 观察者。
        /// 这验证了对以下问题的修复：此前在生命周期事件期间调用 CreateObjectReference 时，
        /// 会因 "Cannot create a local object reference from a grain" 而失败，
        /// 因为这些事件以前是在 SystemTarget 上调度的。
        /// </summary>
        [Fact]
        public async Task LifecycleParticipant_CanCreateGrainObserver()
        {
            var observerCreated = false;
            var grainCalled = false;

            // 创建测试集群，其中有一个会创建观察者的生命周期参与者
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
                // 验证观察者是在参与生命周期时创建的
                Assert.True(observerCreated, "Observer should have been created during lifecycle participation");

                // 验证参与生命周期时成功发起了 Grain 调用
                Assert.True(grainCalled, "Grain should have been called during lifecycle participation");

                // 另外在启动后通过调用 Grain 验证集群正常运行
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
        /// 在 Active 阶段创建 Grain 观察者的生命周期参与者。
        /// 这模拟了问题场景：托管服务或生命周期参与者
        /// 需要在 Silo 启动期间创建 Grain 观察者。
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
                // 这是关键测试：现在生命周期事件通过 Task.Run 执行，
                // 而不是在 SystemTarget 上执行，因此参与生命周期时创建 Grain 观察者应当可行
                var observer = new TestObserver();
                var reference = _grainFactory.CreateObjectReference<ISimpleGrainObserver>(observer);

                // 验证引用已成功创建
                Assert.NotNull(reference);
                _onObserverCreated();

                // 同时测试能否在参与生命周期时发起 Grain 调用
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
        /// 用于测试的简单观察者实现。
        /// </summary>
        private class TestObserver : ISimpleGrainObserver
        {
            public void StateChanged(int a, int b)
            {
                // 用于测试的空操作
            }
        }
    }
}
