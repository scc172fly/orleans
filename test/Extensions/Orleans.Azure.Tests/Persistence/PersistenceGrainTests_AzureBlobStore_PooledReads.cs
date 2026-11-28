using Orleans.Configuration;
using Orleans.Storage;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace Tester.AzureUtils.Persistence;

/// <summary>
/// 使用 AzureStore 和非流式序列化器的 PersistenceGrainTests，用于测试池化读取缓冲区。
/// </summary>
[TestCategory("Persistence"), TestCategory("AzureStorage")]
[TestSuite("Functional")]
[TestProvider("AzureStorage")]
[TestArea("Persistence")]
public class PersistenceGrainTests_AzureBlobStore_PooledReads : Base_PersistenceGrainTests_AzureStore, IClassFixture<PersistenceGrainTests_AzureBlobStore_PooledReads.Fixture>
{
    public class Fixture : BaseAzureTestClusterFixture
    {
        private class StorageSiloBuilderConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder
                    .AddAzureBlobGrainStorage("GrainStorageForTest", optionsBuilder =>
                    {
                        optionsBuilder.Configure(options =>
                        {
                            options.ConfigureTestDefaults();
                        });
                        optionsBuilder.Configure<IGrainStorageSerializer>((options, serializer) =>
                        {
                            // 使用非流式包装器，确保测试覆盖池化缓冲区路径。
                            options.GrainStorageSerializer = new NonStreamingGrainStorageSerializer(serializer);
                        });
                    })
                    .AddMemoryGrainStorage("MemoryStore")
                    .AddMemoryGrainStorage("test1");
            }
        }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.Options.InitialSilosCount = 4;
            builder.Options.UseTestClusterMembership = false;
            builder.AddSiloBuilderConfigurator<SiloBuilderConfigurator>();
            builder.AddSiloBuilderConfigurator<StorageSiloBuilderConfigurator>();
            builder.AddClientBuilderConfigurator<ClientBuilderConfigurator>();
        }
    }

    public PersistenceGrainTests_AzureBlobStore_PooledReads(ITestOutputHelper output, Fixture fixture) : base(output, fixture)
    {
        fixture.EnsurePreconditionsMet();
    }

}
