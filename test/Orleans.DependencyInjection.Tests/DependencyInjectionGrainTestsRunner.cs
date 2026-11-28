using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DependencyInjection.Tests
{
    /// <summary>
    /// Orleans 依赖注入集成测试运行器。
    /// 
    /// Orleans 集成 Microsoft.Extensions.DependencyInjection，以支持：
    /// - Grain 的构造函数注入
    /// - 作用域服务（每个 Grain 激活实例一个服务实例）
    /// - 单例服务（由所有 Grain 共享）
    /// - 与第三方 DI 容器集成
    /// 
    /// 测试的关键概念：
    /// - Grain 的构造函数可以注入依赖项
    /// - 每个 Grain 激活实例拥有自己的作用域
    /// - IGrainFactory 和 IGrainActivationContext 自动可用
    /// - 泛型 Grain 可以根据类型参数获得相应的依赖项
    /// - 按设计不支持在 DI 中显式注册 Grain
    /// 
    /// 此抽象基类包含核心测试逻辑，而具体
    /// 实现用于测试与不同 DI 容器的集成。
    /// </summary>
    public abstract class DependencyInjectionGrainTestRunner : OrleansTestingBase
    {
        private readonly BaseTestClusterFixture fixture;

        /// <summary>
        /// 为依赖注入测试配置服务。
        /// 所有 DI 测试都需要这些服务，因此在基类测试运行器中配置；
        /// 不同的 ServiceProviderFactory 则在具体测试类中设置。
        /// </summary>
        protected class TestSiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IReducer<string, Reducer1Action>>(x => new Reducer1());
                    services.AddSingleton<IReducer<int, Reducer2Action>>(x => new Reducer2());
                    services.AddSingleton<IInjectedService, InjectedService>();
                    services.AddScoped<IInjectedScopedService, InjectedScopedService>();

                    // 显式注册一个 Grain 类，以测试该注册不会被使用。
                    // Orleans 通过自己的激活系统创建 Grain，而不是通过 DI 容器直接解析 Grain。
                    // 此注册用于验证 Orleans 会忽略它，并在
                    // 尝试创建该 Grain 时因缺少构造函数参数而失败。
                    services.AddTransient(
                        sp => new ExplicitlyRegisteredSimpleDIGrain(
                            sp.GetRequiredService<IInjectedService>(),
                            "some value",
                            5));
                });
            }
        }

        public DependencyInjectionGrainTestRunner(BaseTestClusterFixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        /// 验证具有构造函数依赖项的 Grain 能否激活的基础测试。
        /// 此测试验证基本 DI 集成是否正常工作。
        /// </summary>
        [Fact]
        public async Task CanGetGrainWithInjectedDependencies()
        {
            IDIGrainWithInjectedServices grain = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var _ = await grain.GetLongValue();
        }

        /// <summary>
        /// 测试 IGrainFactory 是否自动可用于注入。
        /// IGrainFactory 是一种框架服务，允许 Grain 创建其他 Grain 的引用。
        /// 注意：不要注册自己的 IGrainFactory，Orleans 会自动提供它。
        /// </summary>
        [Fact]
        public async Task CanGetGrainWithInjectedGrainFactory()
        {
            // 请不要在 Startup 类中将自定义 IGrainFactory 实现注入 DI 容器，
            // 因为当前不支持替换 IGrainFactory。
            IDIGrainWithInjectedServices grain = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            _ = await grain.GetGrainFactoryId();
        }

        /// <summary>
        /// 验证单例服务是否由所有 Grain 激活实例共享。
        /// 不同 Grain 应注入相同的服务实例。
        /// </summary>
        [Fact]
        public async Task CanResolveSingletonDependencies()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            // 仅当服务为同一实例时，注入的服务才会返回相同的值
            Assert.Equal(
                await grain1.GetInjectedSingletonServiceValue(),
                await grain2.GetInjectedSingletonServiceValue());

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 验证作用域服务是否为每个 Grain 激活实例创建独立实例。
        /// 每个 Grain 应获得自己的作用域服务实例。
        /// 这对维护每个 Grain 独立状态的服务至关重要。
        /// </summary>
        [Fact]
        public async Task CanResolveScopedDependencies()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            // 仅当服务为不同实例时，注入的服务才会返回不同的值
            Assert.NotEqual(
                await grain1.GetInjectedScopedServiceValue(),
                await grain2.GetInjectedScopedServiceValue());

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 测试 IGrainActivationContext 是否可用作作用域服务。
        /// 每个 Grain 激活实例都有自己的上下文，包含该 Grain 特有的信息，
        /// 例如 GrainId，这些信息应可通过 DI 访问。
        /// </summary>
        [Fact]
        public async Task CanResolveScopedGrainActivationContext()
        {
            long id1 = GetRandomGrainId();
            long id2 = GetRandomGrainId();
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(id1);
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(id2);

            // 仅当服务为不同实例时，注入的服务才会返回不同的值
            Assert.Contains(id1.ToString("X"), await grain1.GetStringValue());
            Assert.Contains(id2.ToString("X"), await grain2.GetStringValue());

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 验证作用域服务在 Grain 激活实例内的线程安全性。
        /// 对同一 Grain 的多个并发调用应使用相同的
        /// 作用域服务实例，因为 Grain 按顺序处理请求。
        /// </summary>
        [Fact]
        public async Task ScopedDependenciesAreThreadSafe()
        {
            const int parallelCalls = 10;
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            var calls =
                Enumerable.Range(0, parallelCalls)
                    .Select(i => grain1.GetInjectedScopedServiceValue())
                    .ToList();

            await Task.WhenAll(calls);
            string expected = await calls[0];
            foreach (var callTask in calls)
            {
                Assert.Equal(expected, await callTask);
            }

            await grain1.DoDeactivate();
        }

        /// <summary>
        /// 测试 Grain 能否通过 IServiceProvider 访问 DI 容器。
        /// 对同一 Grain 激活实例而言，直接从服务提供程序解析的服务应与
        /// 通过构造函数注入的服务一致。
        /// </summary>
        [Fact]
        public async Task CanResolveSameDependenciesViaServiceProvider()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            await grain1.AssertCanResolveSameServiceInstances();
            await grain2.AssertCanResolveSameServiceInstances();

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 验证 IGrainFactory 是否注册为单例。
        /// Silo 中的所有 Grain 应收到相同的 IGrainFactory 实例。
        /// </summary>
        [Fact]
        public async Task CanResolveSingletonGrainFactory()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            // 仅当注入的 Grain 工厂为同一实例时，它才会返回相同的值，
            Assert.Equal(
                await grain1.GetGrainFactoryId(),
                await grain2.GetGrainFactoryId());
        }

        /// <summary>
        /// 测试 DI 中显式注册的 Grain 类是否被忽略。
        /// Orleans 必须通过自身的激活系统创建 Grain，而不是由 DI 直接创建，
        /// 以确保正确的生命周期管理和分布式行为。
        /// 此测试验证显式注册未被使用。
        /// </summary>
        [Fact]
        public async Task CannotGetExplictlyRegisteredGrain()
        {
            ISimpleDIGrain grain = this.fixture.GrainFactory.GetGrain<ISimpleDIGrain>(GetRandomGrainId(), grainClassNamePrefix: "UnitTests.Grains.ExplicitlyRegistered");
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GetLongValue());
            var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("Unable to resolve service for type 'System.String' while attempting to activate 'UnitTests.Grains.ExplicitlyRegisteredSimpleDIGrain'", innerException.Message);
        }

        /// <summary>
        /// 测试泛型 Grain 能否根据其类型参数注入相应的依赖项。
        /// 例如，针对不同的 TState/TAction 组合，可以解析不同的
        /// IReducer&lt;TState, TAction&gt; 实现，从而实现类型安全的依赖注入。
        /// </summary>
        [Fact]
        public async Task CanUseGenericArgumentsInConstructor()
        {
            var grain = this.fixture.GrainFactory.GetGrain<IReducerGameGrain<string, Reducer1Action>>("reducer1");
            Assert.NotNull(await grain.Go("378", new Reducer1Action()));
            var grain2 = this.fixture.GrainFactory.GetGrain<IReducerGameGrain<int, Reducer2Action>>("reducer1");
            Assert.NotEqual(0, await grain2.Go(378, new Reducer2Action()));
        }
    }
}
