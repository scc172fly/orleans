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
    /// 测试 Orleans Grain 调用之间的分布式跟踪及 Activity 传播。
    ///
    /// Orleans 通过与 OpenTelemetry 兼容的 .NET Activity API 支持分布式跟踪。
    /// 这些测试验证：
    /// - Activity 上下文（跟踪 ID、跟踪跨度 ID、跟踪状态）从客户端正确传播到 Grain
    /// - 同时支持 W3C 和分层 Activity ID 格式
    /// - Baggage 项（键值对）正确跨越 Grain 边界传输
    /// - 无论从外部客户端还是在 Grain 之间，Activity 传播均能正常工作
    ///
    /// ActivityPropagationGrainCallFilter 负责为 Grain 调用创建子 Activity，
    /// 并确保上下文在整个分布式系统中正确传播。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ActivityPropagationTests : OrleansTestingBase, IClassFixture<ActivityPropagationTests.Fixture>, IDisposable
    {
        private static readonly ActivityListener Listener;

        static ActivityPropagationTests()
        {
            // 配置 ActivityListener 以监视 Grain 调用的 Activity
            // 此侦听器专门监视 Orleans 为 Grain 调用创建的 Activity
            Listener = new()
            {
                ShouldListenTo = p => p.Name == ActivitySources.ApplicationGrainActivitySourceName,
                Sample = Sample,
                SampleUsingParentId = SampleUsingParentId,
            };

            static ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
            {
                // 采样期间访问 TraceId 很重要，这可确保 Activity 系统
                // 正确初始化跟踪上下文。这复现了一个特定场景：
                // 如果不访问 TraceId，SetParentId 可能无法正常工作。
                var _ = options.TraceId;
                return ActivitySamplingResult.PropagationData;
            }
            ;

            static ActivitySamplingResult SampleUsingParentId(ref ActivityCreationOptions<string> options)
            {
                //必须在采样时访问跟踪 ID，才能复现 SetParentId 无法正常工作的场景
                var _ = options.TraceId;
                return ActivitySamplingResult.PropagationData;
            }
            ;
        }

        /// <summary>
        /// 配置启用 Activity 传播的 Orleans 集群的测试夹具。
        /// Silo 和客户端均已配置为支持分布式跟踪。
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
                        // 在 Silo 上启用 Activity 传播，以支持分布式跟踪
                        .AddActivityPropagation()
                        // 配置用于测试的内存存储提供程序
                        .AddMemoryGrainStorageAsDefault()
                        .AddMemoryGrainStorage("PubSubStore");
            }

            private class ClientConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
                    clientBuilder
                        // 在客户端上启用 Activity 传播，使其参与分布式跟踪
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
        /// 测试不存在父 Activity 时，Grain 调用能否创建新 Activity。
        /// 验证 W3C（标准格式）和分层（旧版 .NET 格式）两种 Activity ID 格式。
        /// 没有父 Activity 时，Orleans 会为 Grain 调用创建新的根 Activity。
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
        /// 测试存在父 Activity 时使用 W3C 格式的 Activity 传播。
        /// 验证以下情况：
        /// - 父 Activity 的跟踪 ID 在 Grain 内得到保留
        /// - 跟踪状态（特定于供应商的跟踪数据）得到正确传播
        /// - Baggage 项（上下文键值对）被传输至 Grain
        /// 确保分布式跟踪能够跨越客户端与 Grain 之间的边界。
        /// </summary>
        [Fact]
        [TestCategory("BVT")]
        public async Task WithParentActivity_W3C()
        {
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;

            var activity = new Activity("SomeName")
            {
                // W3C 传播器要求跟踪状态采用 key=value 格式。
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
                Assert.Contains(activity.TraceId.ToHexString(), result.Id); // 确保跟踪 ID 得到保留。
                Assert.Equal(activity.TraceStateString, result.TraceState);
                Assert.Equal(
                    activity.Baggage.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value!)),
                    result.Baggage);
            }
        }

        /// <summary>
        /// 测试分层格式（旧版 .NET 格式）的 Activity 传播。
        /// 默认 W3C 传播器无法注入分层 Activity，因此 Grain
        /// 会启动新的根 Activity，而非调用方 Activity 的子级。
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
