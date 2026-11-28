using Microsoft.Extensions.Logging;
using Orleans.EventSourcing.CustomStorage;
using Orleans.Storage;
using Orleans.TestingHost;
using TestExtensions;

namespace Tester.EventSourcingTests
{
    /// <summary>
    /// 对事件溯源测试使用专用测试夹具，
    /// 以便添加所需的日志一致性提供程序，并
    /// 执行更多跟踪记录
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
                // 使用经过人为减速的内存存储提供程序
                hostBuilder
                    .AddLogStorageBasedLogConsistencyProvider("LogStorage")
                    .AddStateStorageBasedLogConsistencyProvider("StateStorage")
                    .AddCustomStorageBasedLogConsistencyProvider("CustomStoragePrimaryCluster")
                    .ConfigureLogging(builder =>
                    {
                        // 捕获工作者阶段、进入存储提供程序及存储 Grain 接收请求的事件，用于 #11398。
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
