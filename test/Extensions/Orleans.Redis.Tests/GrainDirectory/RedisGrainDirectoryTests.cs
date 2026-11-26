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
    /// 试验 Orleans grain 使用 Redis 作为目录存储后端 。
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
