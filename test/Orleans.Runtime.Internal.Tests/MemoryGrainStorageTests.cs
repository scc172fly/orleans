using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans.TestingHost;
using TestExtensions;
using TestExtensions.Runners;
using Xunit;

namespace UnitTests;

/// <summary>
/// 测试内存 Grain 存储提供程序。
/// 
/// 内存存储提供程序的特点：
/// - 仅在内存中存储 Grain 状态（非持久化）。
/// - Silo 重启时状态丢失。
/// - 适用于开发、测试和缓存场景。
/// - 支持所有标准持久化操作（Read、Write、Clear）。
/// - 可以配置多个命名实例。
/// 
/// 这些测试验证内存存储能否正确实现
/// IGrainStorage 接口并处理所有持久化操作，
/// 尽管该存储不能跨重启持久保留数据。
/// 
/// 这些测试继承 GrainPersistenceTestsRunner，它提供
/// 一整套持久化行为测试。
/// </summary>
[TestCategory("Persistence"), TestCategory("Memory")]
[TestSuite("Functional")]
[TestProvider("None")]
[TestArea("Runtime")]
public class MemoryGrainStorageTests : GrainPersistenceTestsRunner, IClassFixture<MemoryGrainStorageTests.Fixture>
{
    public class Fixture : BaseTestClusterFixture
    {
        private class StorageSiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                // 配置多个命名内存存储提供程序。
                // Grain 可通过 [StorageProvider] 特性指定要使用的提供程序。
                hostBuilder.AddMemoryGrainStorage("GrainStorageForTest")
                .AddMemoryGrainStorage("test1")
                .AddMemoryGrainStorage("MemoryStore");
            }
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<StorageSiloBuilderConfigurator>();
        }
    }

    public MemoryGrainStorageTests(ITestOutputHelper output, Fixture fixture) : base(output, fixture)
    {
        fixture.EnsurePreconditionsMet();
        // 内存存储不具备持久性，重启后状态丢失。
        // 此标志通知基础测试运行器跳过持久性测试。
        IsDurableStorage = false;
    }
}
