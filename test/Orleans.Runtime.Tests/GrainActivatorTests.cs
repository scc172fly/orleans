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
    /// 测试自定义 grain 活化器,允许对 grain 即时和处置进行完全控制。
    /// 
    /// Orleans 使用 IGrainActivator 创建和销毁 grain 实例。默认情况下,它使用
    /// 辅助注射,但应用可以提供定制的活性动力,用于下列情景:
    /// - 将目标集中用于昂贵的 grain 案例
    /// - 建筑商不能做到的自定义初始化逻辑
    /// - 与第三方DI集装箱合并
    /// - 期间专门化清理 grain 停止停用
    /// 
    /// 此测试显示, 使用完全绕过DI 的自定义活性器
    /// 并跟踪已处理案件的数量。
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
                        // 注册我们的自定义 grain 激活器, 将其作为 grain 类型部件配置
                        // 从而允许它有选择性地适用于特定 grain 类型
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
        /// 证明自定义 grain 启动器可在不使用DI的情况下创建 grain 实例。
        /// 自定义活性器输入一个硬编码值,以证明它被使用
        /// 的默认 DI 激活。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task CanUseCustomGrainActivatorToCreateGrains()
        {
            ISimpleDIGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(GetRandomGrainId(), grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");
            var actual = await grain.GetStringValue();
            Assert.Equal(HardcodedGrainActivator.HardcodedValue, actual);
        }

        /// <summary>
        /// 测试自定义 grain 启动器在 grains 被停用时收到处置通知。
        /// 这对于资源清理方案至关重要,例如将对象退回集合体,
        /// 测试验证处置清点
        /// 明确停用后增加。
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
        /// 自定义 grain 活动器,完全绕过依赖注射。
        /// 既执行 IGrainActivator (用于创建/处置)又执行 IConfigureGrainTypeComponents
        /// (登记具体 grain 类型)。
        /// - 以自定义逻辑而不是DI 创建 grains
        /// - 处理等轨道生命周期事件
        /// - 选择性地适用于特定 grain 使用类型 grain 类图
        /// </summary>
        private class HardcodedGrainActivator : IGrainActivator, IConfigureGrainTypeComponents
        {
            public const string HardcodedValue = "Hardcoded Test Value";
            private readonly GrainClassMap _grainClassMap;
            private int _released;  // 处置 grain 实例的音轨数

            public HardcodedGrainActivator(GrainClassMap grainClassMap)
            {
                _grainClassMap = grainClassMap;
            }

            public void Configure(GrainType grainType, GrainProperties properties, GrainTypeSharedContext shared)
            {
                // 选择注册此激活器, 只为 ExplicitlyRegisteredSimpleDIGrain 类型
                // grain 其它类型将继续使用默认的基于 DI 的驱动器
                if (_grainClassMap.TryGetGrainClass(grainType, out var grainClass) && grainClass == typeof(ExplicitlyRegisteredSimpleDIGrain))
                {
                    shared.SetComponent<IGrainActivator>(this);
                }
            }

            public object CreateInstance(IGrainContext context)
            {
                ActivationOrderingState.Instance.ObserveConstruction(context);

                // 自定义即时逻辑 - 创建 grain , 具有硬编码的依赖性
                // 实际情况下,这可以从一个池子里得到物体, 进行复杂的工作
                // 初始化或与外部系统整合
                return new ExplicitlyRegisteredSimpleDIGrain(new InjectedService(NullLoggerFactory.Instance), HardcodedValue, _released);
            }

            public ValueTask DisposeInstance(IGrainContext context, object instance)
            {
                // 停止使用 grain 时调用,完全可以清理,可返回水池;
                // 计算允许测试来核查处置情况。
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
