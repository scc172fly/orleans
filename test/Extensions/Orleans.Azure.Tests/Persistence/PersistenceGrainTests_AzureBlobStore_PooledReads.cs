using Orleans.Configuration;
using Orleans.Storage;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace Tester.AzureUtils.Persistence;

/// <summary>
/// PersistenceGrainTests 使用 AzureStore ,并配有非流读序列器,该机可使用集合阅读缓冲。
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
                            // 使用非流散包装纸,确保采用集合缓冲路径。
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
