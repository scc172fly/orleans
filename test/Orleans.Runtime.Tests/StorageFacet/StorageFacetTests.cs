using TestExtensions;
using Xunit;
using Orleans.TestingHost;
using Tester.StorageFacet.Infrastructure;
using Tester.StorageFacet.Implementations;

namespace Tester
{
    /// <summary>
    /// 测试具有多个存储实现（Blob 和 Table）的存储分面基础设施，演示可扩展存储模式。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class StorageFacetTests : IClassFixture<StorageFacetTests.Fixture>
    {
        private readonly Fixture fixture;

        public class Fixture : BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.AddSiloBuilderConfigurator<TestSiloBuilderConfigurator>();
            }

            private class TestSiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    // 设置存储功能基础设施。
                    // - 设置基础设施。
                    // - 设置默认功能实现（可选）

                    // 设置基础设施
                    hostBuilder.UseExampleStorage();
                    // 默认存储功能工厂（可选）
                    hostBuilder.UseAsDefaultExampleStorage<TableExampleStorageFactory>();

                    // 服务需要将希望使用的类型添加到集合中
                    // - 调用各实现程序集的扩展函数，注册其类。

                    // Blob：来自 Blob 扩展程序集
                    hostBuilder.UseBlobExampleStorage("Blob");
                    // Table：来自 Table 扩展程序集
                    hostBuilder.UseTableExampleStorage("Table");
                    // Blarg：来自 Blarg 扩展程序集
                    //builder.UseBlargExampleStorage("Blarg");
                }
            }
        }

        public StorageFacetTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact, TestCategory("BVT"), TestCategory("Facet")]
        public Task ExampleStorageFacetHappyPath()
        {
            return ExampleStorageHappyPath<IStorageFacetGrain>();
        }

        [Fact, TestCategory("BVT"), TestCategory("Facet")]
        public Task ExampleStorageFactoryHappyPath()
        {
            return ExampleStorageHappyPath<IStorageFactoryGrain>();
        }

        private async Task ExampleStorageHappyPath<TGrainInterface>()
            where TGrainInterface : IStorageFacetGrain
        {
            IStorageFacetGrain grain = this.fixture.GrainFactory.GetGrain<TGrainInterface>(0);
            string[] names = await grain.GetNames();
            string[] info = await grain.GetExtendedInfo();
            Assert.Equal(2, names.Length);
            Assert.Equal("FirstState", names[0]);
            Assert.Equal("second", names[1]);
            Assert.Equal(2, info.Length);
            Assert.Equal("Blob:FirstState, StateType:String", info[0]);
            Assert.Equal("Table:second-ActivateCalled:True, StateType:String", info[1]);
        }

        [Fact, TestCategory("BVT"), TestCategory("Facet")]
        public Task ExampleStorageFacetDefaultPath()
        {
            return ExampleStorageDefaultPath<IStorageDefaultFacetGrain>();
        }

        [Fact, TestCategory("BVT"), TestCategory("Facet")]
        public Task ExampleStorageFactoryDefaultPath()
        {
            return ExampleStorageDefaultPath<IStorageDefaultFactoryGrain>();
        }

        private async Task ExampleStorageDefaultPath<TGrainInterface>()
            where TGrainInterface : IStorageFacetGrain
        {
            IStorageFacetGrain grain = this.fixture.GrainFactory.GetGrain<TGrainInterface>(0);
            string[] names = await grain.GetNames();
            string[] info = await grain.GetExtendedInfo();
            Assert.Equal(2, names.Length);
            Assert.Equal("FirstState", names[0]);
            Assert.Equal("second", names[1]);
            Assert.Equal(2, info.Length);
            Assert.Equal("Table:FirstState-ActivateCalled:True, StateType:String", info[0]);
            Assert.Equal("Table:second-ActivateCalled:True, StateType:String", info[1]);
        }
    }
}
