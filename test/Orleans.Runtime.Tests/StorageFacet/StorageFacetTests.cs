using TestExtensions;
using Xunit;
using Orleans.TestingHost;
using Tester.StorageFacet.Infrastructure;
using Tester.StorageFacet.Implementations;

namespace Tester
{
    /// <summary>
    /// 测试储存面基础设施,并采用多个存储实施(布和表),以证明可扩展的存储模式。
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
                    // - 设定默认特性执行 - 可选

                    // 基础设施设置
                    hostBuilder.UseExampleStorage();
                    // 默认存储特性工厂 - 可选
                    hostBuilder.UseAsDefaultExampleStorage<TableExampleStorageFactory>();

                    // 服务需要添加要用于收藏的种类
                    // - 向每个执行大会发出推广职能通知,登记其班级。

                    // Blob - 来自 blob 扩展组件
                    hostBuilder.UseBlobExampleStorage("Blob");
                    // 表 - 从表延期组装件
                    hostBuilder.UseTableExampleStorage("Table");
                    // 布拉格 - 来自朗朗朗扩展组装
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
