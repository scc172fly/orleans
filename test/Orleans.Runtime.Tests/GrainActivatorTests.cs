using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Metadata;
using Orleans.Runtime;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 测试自定义 Grain 激活器，它允许完全控制 Grain 实例化和释放。
    /// 
    /// Orleans 使用 IGrainActivator 创建和销毁 Grain 实例。默认情况下使用
    /// 依赖注入，但应用可以为下列场景提供自定义激活器：
    /// - 对创建成本较高的 Grain 实例进行对象池化
    /// - 无法在构造函数中完成的自定义初始化逻辑
    /// - 与第三方 DI 容器集成
    /// - Grain 停用期间的专门清理工作
    /// 
    /// 此测试演示如何实现完全绕过 DI 的自定义激活器，
    /// 并跟踪已释放实例的数量。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("DI")]
    public class GrainActivatorTests : OrleansTestingBase, IClassFixture<GrainActivatorTests.Fixture>
    {
        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<TestSiloBuilderConfigurator>();
            }

            private class TestSiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder.ConfigureServices(services =>
                    {
                        // 将自定义 Grain 激活器注册为 Grain 类型组件配置器
                        // 这样就可以有选择地将其应用于特定 Grain 类型
                        services.AddSingleton<IConfigureGrainTypeComponents, HardcodedGrainActivator>();
                        services.AddSingleton<IConfigureGrainContextProvider, ActivationOrderingConfiguratorProvider>();
                    });
                }
            }
        }

        public GrainActivatorTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        /// 验证自定义 Grain 激活器能够在不使用 DI 的情况下创建 Grain 实例。
        /// 自定义激活器注入一个硬编码值，以证明使用了该激活器，
        /// 而非默认的基于 DI 的激活机制。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task CanUseCustomGrainActivatorToCreateGrains()
        {
            ISimpleDIGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(GetRandomGrainId(), grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");
            var actual = await grain.GetStringValue();
            Assert.Equal(HardcodedGrainActivator.HardcodedValue, actual);
        }

        /// <summary>
        /// 测试 Grain 停用时自定义 Grain 激活器是否接收到释放通知。
        /// 这对于资源清理场景至关重要，例如将对象归还对象池、
        /// 关闭连接或更新指标。测试验证显式停用后
        /// 释放计数是否增加。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task CanUseCustomGrainActivatorToReleaseGrains()
        {
            ISimpleDIGrain grain1 = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(GetRandomGrainId(), grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");
            long initialReleasedInstances = await grain1.GetLongValue();

            ISimpleDIGrain grain2 = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(GetRandomGrainId(), grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");
            long secondReleasedInstances = await grain2.GetLongValue();

            Assert.Equal(initialReleasedInstances, secondReleasedInstances);

            await grain1.DoDeactivate();
            await Task.Delay(250, TestContext.Current.CancellationToken);

            ISimpleDIGrain grain3 = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(GetRandomGrainId(), grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");
            long finalReleasedInstances = await grain3.GetLongValue();
            Assert.Equal(initialReleasedInstances + 1, finalReleasedInstances);
        }

        [Fact, TestCategory("BVT")]
        public async Task GrainContextIsConfiguredBeforeGrainConstruction()
        {
            var state = ActivationOrderingState.Instance;
            state.Arm();
            var grain = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(
                GetRandomGrainId(),
                grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");

            await grain.GetStringValue();

            Assert.True(state.WasConfiguredAtConstruction);
        }

        /// <summary>
        /// 完全绕过依赖注入的自定义 Grain 激活器。
        /// 同时实现 IGrainActivator（用于创建和释放）和 IConfigureGrainTypeComponents
        /// （将自身注册到特定 Grain 类型）。此示例演示如何：
        /// - 使用自定义逻辑而非 DI 来创建 Grain
        /// - 跟踪释放等生命周期事件
        /// - 使用 Grain 类映射有选择地应用于特定 Grain 类型
        /// </summary>
        private class HardcodedGrainActivator : IGrainActivator, IConfigureGrainTypeComponents
        {
            public const string HardcodedValue = "Hardcoded Test Value";
            private readonly GrainClassMap _grainClassMap;
            private int _released;  // 跟踪已释放的 Grain 实例数量

            public HardcodedGrainActivator(GrainClassMap grainClassMap)
            {
                _grainClassMap = grainClassMap;
            }

            public void Configure(GrainType grainType, GrainProperties properties, GrainTypeSharedContext shared)
            {
                // 仅对 ExplicitlyRegisteredSimpleDIGrain 类型注册此激活器
                // 其他 Grain 类型继续使用默认的基于 DI 的激活器
                if (_grainClassMap.TryGetGrainClass(grainType, out var grainClass) && grainClass == typeof(ExplicitlyRegisteredSimpleDIGrain))
                {
                    shared.SetComponent<IGrainActivator>(this);
                }
            }

            public object CreateInstance(IGrainContext context)
            {
                ActivationOrderingState.Instance.ObserveConstruction(context);

                // 自定义实例化逻辑：使用硬编码依赖项创建 Grain
                // 在实际场景中，这里可以从对象池获取对象、执行复杂的
                // 初始化，或者与外部系统集成
                return new ExplicitlyRegisteredSimpleDIGrain(new InjectedService(NullLoggerFactory.Instance), HardcodedValue, _released);
            }

            public ValueTask DisposeInstance(IGrainContext context, object instance)
            {
                // Grain 停用时调用：非常适合清理、归还对象池
                // 或更新指标。计数使测试能够验证释放确已发生。
                ++_released;
                return default;
            }
        }

        private sealed class ActivationOrderingConfiguratorProvider(GrainClassMap grainClassMap) : IConfigureGrainContextProvider
        {
            public bool TryGetConfigurator(
                GrainType grainType,
                GrainProperties properties,
                [NotNullWhen(true)] out IConfigureGrainContext? configurator)
            {
                if (grainClassMap.TryGetGrainClass(grainType, out var grainClass)
                    && grainClass == typeof(ExplicitlyRegisteredSimpleDIGrain))
                {
                    configurator = ActivationOrderingState.Instance;
                    return true;
                }

                configurator = null;
                return false;
            }
        }

        private sealed class ActivationOrderingState : IConfigureGrainContext
        {
            private int _armed;
            private int _wasConfiguredAtConstruction;

            public static ActivationOrderingState Instance { get; } = new();

            public bool WasConfiguredAtConstruction => Volatile.Read(ref _wasConfiguredAtConstruction) != 0;

            public void Arm()
            {
                Volatile.Write(ref _wasConfiguredAtConstruction, 0);
                Volatile.Write(ref _armed, 1);
            }

            public void Configure(IGrainContext context)
            {
                if (Volatile.Read(ref _armed) == 0)
                {
                    return;
                }

                context.SetComponent(ConfiguredContextMarker.Instance);
            }

            public void ObserveConstruction(IGrainContext context)
            {
                if (Interlocked.Exchange(ref _armed, 0) == 0)
                {
                    return;
                }

                Volatile.Write(
                    ref _wasConfiguredAtConstruction,
                    context.GetComponent<ConfiguredContextMarker>() is not null ? 1 : 0);
            }
        }

        private sealed class ConfiguredContextMarker
        {
            public static ConfiguredContextMarker Instance { get; } = new();
        }
    }
}
