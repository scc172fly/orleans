using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DependencyInjection.Tests
{
    /// <summary>
    /// 测试 Orleans 依赖性注射综合测试的选手。
    /// 
    /// Orleans 与 Microsoft.Extensions.DependencyInjection 结合,以支持:
    /// - grains中构造注入
    /// - 范围服务(每启动 grain 一个实例)
    /// - 单吨服务(分摊所有国家 grains)
    /// - 与第三方DI集装箱合并
    /// 
    /// 测试的关键概念:
    /// - Grain 建筑商可采用依赖性注入
    /// - 每一次 grain 启动都有其范围
    /// - IGrainFactory 和 IGrainActivationContext 是自动可用的
    /// - 通用 grains 可根据其类型参数具有依赖性
    /// - 设计不支持在DI中明确 grain 注册的证明
    /// 
    /// 这个抽象的基级包含核心测试逻辑, 而混凝土
    /// 测试不同的DI集装箱集成。
    /// </summary>
    public abstract class DependencyInjectionGrainTestRunner : OrleansTestingBase
    {
        private readonly BaseTestClusterFixture fixture;

        /// <summary>
        /// 配置辅助注射测试服务。
        /// 这是在基础测试运行器里 因为所有DI测试都需要这些服务
        /// 不同的 ServiceProviderFactory 设置在混凝土测试类中。
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

                    // 明确登记一个 grain 类,以测试它不会使用该注册。
                    // Orleans 通过自己的激活系统而不是DI容器创建 grains 。
                    // 本次注册是为了验证 Orleans 忽略了它, 并且失败
                    // 尝试创建 grain (缺失构建参数)时。
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
        /// 基本测试,即 grains 可随着建筑商依赖而激活。
        /// 这可以核实基本数据交换整合工作是否有效。
        /// </summary>
        [Fact]
        public async Task CanGetGrainWithInjectedDependencies()
        {
            IDIGrainWithInjectedServices grain = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var _ = await grain.GetLongValue();
        }

        /// <summary>
        /// 测试 IGrainFactory 自动用于注入。
        /// IGrainFactory 是一个框架服务,允许 grains 创建其他 grains。
        /// 注:不要自动登记您自己的 IGrainFactory - Orleans 。
        /// </summary>
        [Fact]
        public async Task CanGetGrainWithInjectedGrainFactory()
        {
            // 请不要在启动类中将 IGrainFactory 的安装投向DI容器, 
            // 由于目前我们不支持替换 IGrainFactory 
            IDIGrainWithInjectedServices grain = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            _ = await grain.GetGrainFactoryId();
        }

        /// <summary>
        /// 证明单吨服务在所有 grain 启动中共享。
        /// 同一情况应注入不同的 grains。
        /// </summary>
        [Fact]
        public async Task CanResolveSingletonDependencies()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            // 只有在注射服务相同的情况下, 才会返回相同的值
            Assert.Equal(
                await grain1.GetInjectedSingletonServiceValue(),
                await grain2.GetInjectedSingletonServiceValue());

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 证明范围化的服务为每次启动 grain 创建了独特的实例。
        /// 每个 grain 应获得自己范围服务的实例。
        /// 这对于维持每个grain 状态的服务至关重要。
        /// </summary>
        [Fact]
        public async Task CanResolveScopedDependencies()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            // 只有在不同的注射情况下, 注入服务才会返回不同值
            Assert.NotEqual(
                await grain1.GetInjectedScopedServiceValue(),
                await grain2.GetInjectedScopedServiceValue());

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 测试 IGrainActivationContext 是否可作为范围服务。
        /// 每项 grain 启动都有与 grain具体信息有关的背景
        /// 类似 GrainId,可通过DI进入。
        /// </summary>
        [Fact]
        public async Task CanResolveScopedGrainActivationContext()
        {
            long id1 = GetRandomGrainId();
            long id2 = GetRandomGrainId();
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(id1);
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(id2);

            // 只有在不同的注射情况下, 注入服务才会返回不同值
            Assert.Contains(id1.ToString("X"), await grain1.GetStringValue());
            Assert.Contains(id2.ToString("X"), await grain2.GetStringValue());

            await grain1.DoDeactivate();
            await grain2.DoDeactivate();
        }

        /// <summary>
        /// 证明在 grain 激活中,范围服务是安全的。
        /// 多个同时调用到相同 grain 的多个调用应该使用相同的
        /// 范围广泛的服务实例,因为 grains 流程按顺序要求。
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
        /// grains 可以通过 IServiceProvider进入DI容器的测试。
        /// 直接由提供者解决的服务应与服务提供者提供的服务相匹配
        /// 通过同一材料的制造器注入 grain 启动
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
        /// 证明 IGrainFactory 已登记为单一吨。
        /// 全部 grains 中在 silo 领取相同 IGrainFactory 实例。
        /// </summary>
        [Fact]
        public async Task CanResolveSingletonGrainFactory()
        {
            var grain1 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());
            var grain2 = this.fixture.GrainFactory.GetGrain<IDIGrainWithInjectedServices>(GetRandomGrainId());

            // 注射 grain 工厂只有在相同的情况下才会返回同值,
            Assert.Equal(
                await grain1.GetGrainFactoryId(),
                await grain2.GetGrainFactoryId());
        }

        /// <summary>
        /// DI 中明确注册的 grain 类测试被忽略。
        /// Orleans 必须通过其激活系统而不是DI, 创建 grains
        /// 确保适当的生命周期管理和分布行为。
        /// 这一检验标准核实明确注册是否未使用。
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
        /// 通用 grains 可根据其类型参数进行有依赖性注入的试验。
        /// 例如,减税<TState, TAction> 不同时可采用不同的解决办法
        /// 国家/行动组合,允许进行安全型号的自养注射。
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
