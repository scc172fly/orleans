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
/// 模拟 grain 存储提供者的测试。
/// 
/// 内存存储供应商的特点:
/// - 仅存储储存 grain 状态(不可耐用)
/// - silo 重新启动时损失状态
/// - 有助于发展、测试和缓存情景
/// - 支持所有标准持久性作业(阅读、写、清除)
/// - 多个已命名的实例可以配置
/// 
/// 这些测试核查内存存储正确执行
/// IGrainStorage 接口和处理所有持续运行,
/// 尽管存储在重新开始时不耐久。
/// 
/// 试验的继承 GrainPersistenceTestsRunner ,该试验规定
/// 一套全面的持久性行为测试
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
                // 配置多个名为内存存储供应商
                // Grains 可指定通过[StorageProvider属性使用哪个供应商
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
        // 内存存储不耐久 - 恢复时丢失状态
        // 国旗让基准测试运行者跳过耐久性测试
        IsDurableStorage = false;
    }
}
