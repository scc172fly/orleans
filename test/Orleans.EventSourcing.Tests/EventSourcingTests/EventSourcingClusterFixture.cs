using Microsoft.Extensions.Logging;
using Orleans.EventSourcing.CustomStorage;
using Orleans.Storage;
using Orleans.TestingHost;
using TestExtensions;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 我们用特别固定装置来进行事件源测试 
    /// 以便我们增加所需的日志一致性提供者, 
    /// 做更多追踪
    /// </summary>
    public class EventSourcingClusterFixture : BaseTestClusterFixture
    {
        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        }

        private class TestSiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                // 我们使用一个缓缓存储存储供应商
                hostBuilder
                    .AddLogStorageBasedLogConsistencyProvider("LogStorage")
                    .AddStateStorageBasedLogConsistencyProvider("StateStorage")
                    .AddCustomStorageBasedLogConsistencyProvider("CustomStoragePrimaryCluster")
                    .ConfigureLogging(builder =>
                    {
                        // 抓捕工人阶段、储存提供者入境和储存—— #11398的grain 收据。
                        builder.AddFilter("Orleans.Runtime.LogConsistency", LogLevel.Debug);
                        builder.AddFilter(typeof(MemoryGrainStorage).Namespace, LogLevel.Trace);
                    })
                    .AddMemoryGrainStorageAsDefault()
                    .AddMemoryGrainStorage("AzureStore")
                    .AddMemoryGrainStorage("MemoryStore")
                    .AddFaultInjectionMemoryStorage("SlowMemoryStore", options => options.NumStorageGrains = 10, faultyOptions => faultyOptions.Latency = TimeSpan.FromMilliseconds(15));
            }
        }
    }
}
