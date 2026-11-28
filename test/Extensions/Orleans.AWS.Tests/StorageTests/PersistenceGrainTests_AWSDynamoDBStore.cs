using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Storage;
using Orleans.TestingHost;
using Microsoft.Extensions.Logging.Abstractions;
using TesterInternal;
using UnitTests.GrainInterfaces;
using Xunit;
using static Orleans.Storage.DynamoDBGrainStorage;
using TestExtensions.Runners;

namespace AWSUtils.Tests.StorageTests
{
    /// <summary>
    /// 测试用于 Grain 持久化的 DynamoDB 存储提供程序，包括 Grain 引用的序列化。
    /// </summary>
    [TestCategory("Persistence"), TestCategory("AWS"), TestCategory("DynamoDb")]
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Persistence")]
    public class PersistenceGrainTests_AWSDynamoDBStore : GrainPersistenceTestsRunner, IClassFixture<PersistenceGrainTests_AWSDynamoDBStore.Fixture>
    {
        public class Fixture : TestExtensions.BaseTestClusterFixture
        {
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                if (AWSTestConstants.IsDynamoDbAvailable)
                {
                    builder.Options.InitialSilosCount = 4;
                    builder.AddSiloBuilderConfigurator<SiloBuilderConfigurator>();
                }
            }

            public class SiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder.AddMemoryGrainStorage("MemoryStore");
                    hostBuilder.AddMemoryGrainStorage("test1");
                    hostBuilder.AddDynamoDBGrainStorage("GrainStorageForTest", options =>
                    {
                        options.Service = AWSTestConstants.DynamoDbService;
                        options.ServiceId = string.Empty;
                    });
                }
            }
        }

        public PersistenceGrainTests_AWSDynamoDBStore(ITestOutputHelper output, Fixture fixture) : base(output, fixture, grainNamespace: "UnitTests.Grains")
        {
            if (!AWSTestConstants.IsDynamoDbAvailable)
            {
                output.WriteLine("Unable to connect to AWS DynamoDB simulator");
                throw Xunit.Sdk.SkipException.ForSkip("Unable to connect to AWS DynamoDB simulator");
            }
        }

        [Fact, TestCategory("Functional")]
        public async Task AWSDynamoDBStore_ConvertToFromStorageFormat_GrainReference()
        {
            // 注意：此测试要求 Silo 正在运行且客户端已初始化，以便在序列化前解析 Grain 引用。
            Guid id = Guid.NewGuid();
            IUser grain = this.HostedCluster.GrainFactory!.GetGrain<IUser>(id); // 此测试需要已初始化的客户端。

            var initialState = new GrainStateContainingGrainReferences { Grain = grain };
            var entity = new GrainStateRecord();
            var storage = await InitDynamoDBTableStorageProvider(
                this.HostedCluster.ServiceProvider.GetRequiredService<IProviderRuntime>(), "TestTable");
            storage.ConvertToStorageFormat(initialState, entity);
            var convertedState = storage.ConvertFromStorageFormat<GrainStateContainingGrainReferences>(entity);
            Assert.NotNull(convertedState); // 转换后的状态
            Assert.Equal(initialState.Grain, convertedState.Grain);  // "Grain"
        }

        [Fact, TestCategory("Functional")]
        public async Task AWSDynamoDBStore_ConvertToFromStorageFormat_GrainReference_List()
        {
            // 注意：此测试要求 Silo 正在运行且客户端已初始化，以便在序列化前解析 Grain 引用。
            Guid[] ids = { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            IUser[] grains = new IUser[3];
            grains[0] = this.HostedCluster.GrainFactory!.GetGrain<IUser>(ids[0]); // 此测试需要已初始化的客户端。
            grains[1] = this.HostedCluster.GrainFactory!.GetGrain<IUser>(ids[1]); // 此测试需要已初始化的客户端。
            grains[2] = this.HostedCluster.GrainFactory!.GetGrain<IUser>(ids[2]); // 此测试需要已初始化的客户端。

            var initialState = new GrainStateContainingGrainReferences();
            foreach (var g in grains)
            {
                initialState.GrainList.Add(g);
                initialState.GrainDict.Add(g.GetPrimaryKey().ToString(), g);
            }
            var entity = new GrainStateRecord();
            var storage =
                await InitDynamoDBTableStorageProvider(
                    this.HostedCluster.ServiceProvider.GetRequiredService<IProviderRuntime>(), "TestTable");
            storage.ConvertToStorageFormat(initialState, entity);
            var convertedState = storage.ConvertFromStorageFormat<GrainStateContainingGrainReferences>(entity);
            Assert.NotNull(convertedState);
            Assert.Equal(initialState.GrainList.Count, convertedState.GrainList.Count);  // "GrainList 大小"
            Assert.Equal(initialState.GrainDict.Count, convertedState.GrainDict.Count);  // "GrainDict 大小"
            for (int i = 0; i < grains.Length; i++)
            {
                string iStr = ids[i].ToString();
                Assert.Equal(initialState.GrainList[i], convertedState.GrainList[i]);  // "GrainList #{0}", i
                Assert.Equal(initialState.GrainDict[iStr], convertedState.GrainDict[iStr]);  // "GrainDict #{0}", i
            }
            Assert.Equal(initialState.Grain, convertedState.Grain);  // "Grain"
        }

        private static async Task<DynamoDBGrainStorage> InitDynamoDBTableStorageProvider(IProviderRuntime runtime, string storageName)
        {
            var options = new DynamoDBStorageOptions();
            options.Service = AWSTestConstants.DynamoDbService;
            options.GrainStorageSerializer = ActivatorUtilities.CreateInstance<OrleansGrainStorageSerializer>(runtime.ServiceProvider);

            DynamoDBGrainStorage store = ActivatorUtilities.CreateInstance<DynamoDBGrainStorage>(runtime.ServiceProvider, "PersistenceGrainTests", options);
            ISiloLifecycleSubject lifecycle = ActivatorUtilities.CreateInstance<SiloLifecycleSubject>(runtime.ServiceProvider, NullLogger<SiloLifecycleSubject>.Instance);
            store.Participate(lifecycle);
            await lifecycle.OnStart();
            return store;
        }
    }
}
