using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Orleans.Diagnostics;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace UnitTests.General
{
    /// <summary>
    /// 分布式追踪和活动测试,通过 Orleans grain 电话进行传播。
    ///
    /// Orleans 支持通过 .NET活动 API进行分布式追踪,该活动与 OpenTelemetry兼容。
    /// 这些测试证实:
    /// - 活动环境(跟踪身份识别、横跨身份识别、追踪状态)从客户向 grain适当传播
    /// - W3C和等级活动身份识别格式都得到支持
    /// - 正确跨越 grain 边界传送行李(钥匙价值对)
    /// - 活动传播工作正确,既从外部客户那里,也从 grains之间进行。
    ///
    /// ActivityPropagationGrainCallFilter 负责为 grain 电话创建儿童活动
    /// 并确保在整个分布式系统适当传播环境。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ActivityPropagationTests : OrleansTestingBase, IClassFixture<ActivityPropagationTests.Fixture>, IDisposable
    {
        private static readonly ActivityListener Listener;

        static ActivityPropagationTests()
        {
            // 配置 ActivityListener 以监测 grain 调用活动
            // 该听众专门针对 Orleans 为 grain 电话创建的活动
            Listener = new()
            {
                ShouldListenTo = p => p.Name == ActivitySources.ApplicationGrainActivitySourceName,
                Sample = Sample,
                SampleUsingParentId = SampleUsingParentId,
            };

            static ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
            {
                // 在取样期间访问 TraceId 对于确保活动系统十分重要
                // 正确初始化跟踪上下文。 这复制了特定的设想情景
                // 何 地 SetParentId 如果 TraceId 无法进入
                var _ = options.TraceId;
                return ActivitySamplingResult.PropagationData;
            }
            ;

            static ActivitySamplingResult SampleUsingParentId(ref ActivityCreationOptions<string> options)
            {
                //在 SetParentId 不工作时,必须用样本访问追踪id才能复制设想情况
                var _ = options.TraceId;
                return ActivitySamplingResult.PropagationData;
            }
            ;
        }

        /// <summary>
        /// 测试设置,以配置 Orleans 组群,并启用活动传播功能。
        /// silo 和客户端都配置了支持分布式跟踪的配置。
        /// </summary>
        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.ConfigureHostConfiguration(TestDefaultConfiguration.ConfigureHostConfiguration);
                builder.AddSiloBuilderConfigurator<SiloInvokerTestSiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<ClientConfigurator>();
            }

            private class SiloInvokerTestSiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder) =>
                    hostBuilder
                        // 启用 silo 上的活动传播以支持分布追踪
                        .AddActivityPropagation()
                        // 用于测试的配置内存存储存储供应商
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("PubSubStore");
            }

            private class ClientConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
                    clientBuilder
                        // 使向客户宣传活动能够参与分布式跟踪
                        .AddActivityPropagation();
            }
        }

        private readonly ActivityIdFormat _defaultIdFormat;
        private readonly Fixture _fixture;

        public ActivityPropagationTests(Fixture fixture)
        {
            _defaultIdFormat = Activity.DefaultIdFormat;
            this._fixture = fixture;
            ActivitySource.AddActivityListener(Listener);
        }

        public void Dispose() => Activity.DefaultIdFormat = _defaultIdFormat;

        /// <summary>
        /// 测试当不存在父活动时 grain 调用创建新活动。
        /// 验证 W3C(标准格式)和等级(传统 .NET 格式)活动身份识别格式。
        /// 当不存在父活动时, Orleans 为 grain 调用创建新的根活动。
        /// </summary>
        [Theory]
        [InlineData(ActivityIdFormat.W3C)]
        [InlineData(ActivityIdFormat.Hierarchical)]
        [TestCategory("BVT")]
        public async Task WithoutParentActivity(ActivityIdFormat idFormat)
        {
            Activity.DefaultIdFormat = idFormat;

            await Test(_fixture.GrainFactory);
            await Test(_fixture.Client);

            static async Task Test(IGrainFactory grainFactory)
            {
                var grain = grainFactory.GetGrain<IActivityGrain>(Random.Shared.Next());

                var result = await grain.GetActivityId();

                Assert.NotNull(result);
                Assert.NotEmpty(result.Id!);
                Assert.Null(result.TraceState);
            }
        }

        /// <summary>
        /// 当父活动存在时, 测试以 W3C 格式传播的活动。
        /// 证明:
        /// - 母体活动踪迹识别码保存在 grain中
        /// - 正确传播追踪状态(具体针对供应商的追查数据)
        /// - 行李(配对的原始钥匙)传送至 grain
        /// 这确保分布的痕迹能够跨越客户到grain 边界。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task WithParentActivity_W3C()
        {
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;

            var activity = new Activity("SomeName")
            {
                // W3C 传播器需要键=值跟踪状态。
                TraceStateString = "vendor=value"
            };
            activity.AddBaggage("foo", "bar");
            activity.Start();

            try
            {
                await Test(_fixture.GrainFactory);
                await Test(_fixture.Client);
            }
            finally
            {
                activity.Stop();
            }

            async Task Test(IGrainFactory grainFactory)
            {
                var grain = grainFactory.GetGrain<IActivityGrain>(Random.Shared.Next());

                var result = await grain.GetActivityId();

                Assert.NotNull(result);
                Assert.NotNull(result.Id);
                Assert.Contains(activity.TraceId.ToHexString(), result.Id); // 确保追踪id得以持续。
                Assert.Equal(activity.TraceStateString, result.TraceState);
                Assert.Equal(
                    activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)),
                    result.Baggage);
            }
        }

        /// <summary>
        /// 以等级格式(传统 .NET 格式)进行测试活动传播。
        /// 默认 W3C 推进器无法输入等级活动, 所以 grain
        /// 开始新的根活动, 而不是调用器活动的子。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task WithParentActivity_Hierarchical()
        {
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;

            var activity = new Activity("SomeName");
            activity.AddBaggage("foo", "bar");
            activity.Start();

            try
            {
                await Test(_fixture.GrainFactory);
                await Test(_fixture.Client);
            }
            finally
            {
                activity.Stop();
            }

            async Task Test(IGrainFactory grainFactory)
            {
                var grain = grainFactory.GetGrain<IActivityGrain>(Random.Shared.Next());

                var result = await grain.GetActivityId();

                Assert.NotNull(result);
                Assert.NotNull(result.Id);
            }
        }
    }
}
