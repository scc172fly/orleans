using Microsoft.Extensions.Configuration;
using Orleans.Streams;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.StreamingTests
{
    /// <summary>
    /// 测试不同 Grain 键类型的隐式流订阅，具体测试使用 long 类型键的 Grain 订阅。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public sealed class ImplicitSubscriptionKeyTypeGrainTests : OrleansTestingBase, IClassFixture<ImplicitSubscriptionKeyTypeGrainTests.Fixture>
    {
        private readonly Fixture fixture;
        private readonly IStreamProvider _streamProvider;

        public class Fixture : BaseTestClusterFixture
        {
            public const string StreamProviderName = GeneratedStreamTestConstants.StreamProviderName;

            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder.AddMemoryGrainStorageAsDefault();

                    hostBuilder.AddMemoryStreams(ImplicitStreamTestConstants.StreamProviderName)
                        .AddMemoryGrainStorage("PubSubStore");
                }
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
                {
                    clientBuilder.AddMemoryStreams(ImplicitStreamTestConstants.StreamProviderName);
                }
            }
        }

        public ImplicitSubscriptionKeyTypeGrainTests(Fixture fixture)
        {
            this.fixture = fixture;
            _streamProvider = fixture.Client.GetStreamProvider(ImplicitStreamTestConstants.StreamProviderName);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming")]
        public async Task LongKey()
        {
            long grainId = 13;
            int value = 87;
            IAsyncStream<int> stream = _streamProvider.GetStream<int>(nameof(IImplicitSubscriptionLongKeyGrain), grainId);

            await stream.OnNextAsync(value);

            var consumer = fixture.GrainFactory.GetGrain<IImplicitSubscriptionLongKeyGrain>(grainId);
            await TestingUtils.WaitUntilAsync(
                (lastTry, cancellationToken) => CheckValue(consumer, value, lastTry, cancellationToken),
                TimeSpan.FromSeconds(30),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        private async Task<bool> CheckValue(IImplicitSubscriptionKeyTypeGrain consumer, int expectedValue, bool assertIsTrue, CancellationToken cancellationToken)
        {
            int value = await consumer.GetValue(cancellationToken);

            if (assertIsTrue)
            {
                Assert.Equal(expectedValue, value);
            }

            if (expectedValue != value)
            {
                return false;
            }

            return true;
        }
    }
}
