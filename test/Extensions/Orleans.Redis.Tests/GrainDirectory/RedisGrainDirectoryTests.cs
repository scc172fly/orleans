using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory.Redis;
using StackExchange.Redis;
using Tester.Directories;
using TestExtensions;
using Xunit;

namespace Tester.Redis.GrainDirectory
{
    /// <summary>
    /// 测试以 Redis 为目录存储后端的 Orleans Grain 目录功能。
    /// </summary>
    [TestCategory("Redis"), TestCategory("Directory"), TestCategory("Functional")]
    [Collection(TestEnvironmentFixture.DefaultCollection)]
    [TestSuite("Functional")]
    [TestProvider("Redis")]
    [TestArea("GrainDirectory")]
    public class RedisGrainDirectoryTests : GrainDirectoryTests<RedisGrainDirectory>
    {
        public RedisGrainDirectoryTests(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        protected override RedisGrainDirectory CreateGrainDirectory()
        {
            TestUtils.CheckForRedis();
            var configuration = TestDefaultConfiguration.RedisConnectionString;
            var directoryOptions = new RedisGrainDirectoryOptions
            {
                ConfigurationOptions = ConfigurationOptions.Parse(configuration!),
                EntryExpiry = TimeSpan.FromMinutes(1),
            };

            var clusterOptions = Options.Create(new ClusterOptions { ServiceId = "SomeServiceId", ClusterId = Guid.NewGuid().ToString("N") });
            var directory = new RedisGrainDirectory(
                directoryOptions,
                clusterOptions,
                this.loggerFactory.CreateLogger<RedisGrainDirectory>());
            directory.Initialize().GetAwaiter().GetResult();
            return directory;
        }
    }
}
