using System.Net;
using Microsoft.Extensions.Hosting;
using Xunit;
using Orleans.Configuration;
using Orleans.Runtime;
using StackExchange.Redis;
using TestExtensions;

namespace Tester.Redis.Persistence
{
    /// <summary>
    /// 测试 Redis Grain 存储的配置验证及设置场景。
    /// </summary>
    [TestCategory("Redis"), TestCategory("Persistence"), TestCategory("Functional")]
    [Collection(TestEnvironmentFixture.DefaultCollection)]
    [TestSuite("Functional")]
    [TestProvider("Redis")]
    [TestArea("Persistence")]
    public class RedisPersistenceSetupTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("localhost:1234")]
        public void StorageOptionsValidator(string? connectionString)
        {
            TestUtils.CheckForRedis();

            var siloPort = 11111;
            int gatewayPort = 30000;
            var siloAddress = IPAddress.Loopback;

            var host = Host.CreateDefaultBuilder()
                .UseOrleans((ctx, builder) =>
                {
                    builder.Configure<ClusterOptions>(options => options.ClusterId = "TESTCLUSTER")
                        .UseDevelopmentClustering(options => options.PrimarySiloEndpoint = new IPEndPoint(siloAddress, siloPort))
                        .ConfigureEndpoints(siloAddress, siloPort, gatewayPort)
                        .AddRedisGrainStorage("Redis", optionsBuilder => optionsBuilder.Configure(options =>
                        {
                            if (connectionString is not null)
                            {
                                options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
                            }
                        }));
                }).Build();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Throws<OrleansConfigurationException>(() => host.Start());
            }
        }
    }
}
