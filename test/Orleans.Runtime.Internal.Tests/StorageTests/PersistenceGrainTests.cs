//#define REREAD_STATE_AFTER_WRITE_FAILED

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;
using TesterInternal;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

// ReSharper disable RedundantAssignment
// ReSharper disable UnusedVariable
// ReSharper disable InconsistentNaming

namespace UnitTests.StorageTests
{
    /// <summary>
    /// PersistenceGrainTests：仅使用本地单元测试 Silo 运行，不依赖外部 Azure 存储。
    /// </summary>
    public class PersistenceGrainTests_Local : OrleansTestingBase, IClassFixture<PersistenceGrainTests_Local.Fixture>, IDisposable, IAsyncLifetime
    {
        public class Fixture : BaseTestClusterFixture
        {

            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.InitialSilosCount = 1;
                builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            }

            private class SiloConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder)
                {
                    hostBuilder.AddMemoryGrainStorage("MemoryStore");
                    hostBuilder.AddTestStorageProvider(MockStorageProviderName1, (sp, name) => ActivatorUtilities.CreateInstance<MockStorageProvider>(sp, name));
                    hostBuilder.AddTestStorageProvider(MockStorageProviderName2, (sp, name) => ActivatorUtilities.CreateInstance<MockStorageProvider>(sp, name));
                    hostBuilder.AddTestStorageProvider(MockStorageProviderNameLowerCase, (sp, name) => ActivatorUtilities.CreateInstance<MockStorageProvider>(sp, name));
                    hostBuilder.AddTestStorageProvider(ErrorInjectorProviderName, (sp, name) => ActivatorUtilities.CreateInstance<ErrorInjectionStorageProvider>(sp));

                    hostBuilder.Services.AddSingleton<OrleansGrainStorageSerializer>();
                    hostBuilder.AddMemoryGrainStorage("OrleansSerializerMemoryStore", (OptionsBuilder<MemoryGrainStorageOptions> optionsBuilder) =>
                        optionsBuilder.Configure<OrleansGrainStorageSerializer>((options, serializer) => options.GrainStorageSerializer = serializer));
                }
            }
        }

        private const string DefaultGrainStateName = "state";
        private const string MockStorageProviderName1 = "test1";
        private const string MockStorageProviderName2 = "test2";
        private const string MockStorageProviderNameLowerCase = "lowercase";
        private const string ErrorInjectorProviderName = "ErrorInjector";
        private readonly ITestOutputHelper output;

        protected TestCluster HostedCluster { get; }

        public PersistenceGrainTests_Local(ITestOutputHelper output, Fixture fixture)
        {
            this.output = output;
            HostedCluster = fixture.HostedCluster;
            ResetMockStorageProvidersHistory();
        }

        public async ValueTask InitializeAsync()
        {
            await SetErrorInjection(ErrorInjectorProviderName, ErrorInjectionPoint.None);
        }

        public async ValueTask DisposeAsync()
        {
            await SetErrorInjection(ErrorInjectorProviderName, ErrorInjectionPoint.None);
        }

        public void Dispose()
        {
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Silo_StorageProvider_Name_Missing()
        {
            List<SiloHandle> silos = this.HostedCluster.GetActiveSilos().ToList();
            var silo = silos.First();
            const string providerName = "NotPresent";
            Assert.False(await this.HostedCluster.Client!.GetTestHooks(silo).HasStorageProvider(providerName),
                    $"Provider {providerName} on silo {silo.Name} should not be registered");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_CheckStateInit()
        {
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);
            bool ok = await grain.CheckStateInit();
            Assert.True(ok, "CheckStateInit OK");
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_CheckStorageProvider()
        {
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);
            string providerType = await grain.CheckProviderType();
            Assert.Equal(typeof(MockStorageProvider).FullName, providerType); // StorageProvider 提供程序类型
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Init()
        {
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);
            await grain.DoSomething();

            //请求此集群所有 Silo 上提供程序的 InitCount。
            IManagementGrain mgmtGrain = this.HostedCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            object?[] replies = await mgmtGrain.SendControlCommandToProvider<MockStorageProvider>(
               MockStorageProviderName1,
               (int)MockStorageProvider.Commands.InitCount,
               null,
               TestContext.Current.CancellationToken);

            Assert.Contains(1, replies); // StorageProvider 初始化次数
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Activate_StoredValue()
        {
            const string providerName = MockStorageProviderName1;
            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");

            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(guid);

            // 将初始值写入存储。
            int initialValue = 567;
            SetStoredValue<MockStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", initialValue);

            int readValue = await grain.GetValue();
            Assert.Equal(initialValue, readValue); // 读取先前存储的值。
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Generics")]
        public async Task Persistence_Grain_Activate_StoredValue_Generic()
        {
            const string providerName = MockStorageProviderName1;
            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");

            var grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGenericGrain<int>>(guid);

            // 将初始值写入存储。
            int initialValue = 567;
            SetStoredValue<MockStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", initialValue);

            int readValue = await grain.GetValue();
            Assert.Equal(initialValue, readValue); // 读取先前存储的值。
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Activate_Error()
        {
            const string providerName = ErrorInjectorProviderName;
            string grainType = typeof(PersistenceProviderErrorGrain).FullName!;
            Guid guid = Guid.NewGuid();
            string id = guid.ToString("N");

            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(guid);

            // 将初始值写入存储。
            int initialValue = 567;
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, grainType, grain, "Field1", initialValue);

            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeRead);

            await Assert.ThrowsAsync<StorageProviderInjectedError>(() =>
                grain.GetValue());
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Read()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);

            await grain.DoSomething();
            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Write()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);

            await grain.DoWrite(1);

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(1, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            await grain.DoWrite(2);
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(2, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(2, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_ReRead()
        {
            const string providerName = MockStorageProviderName1;

            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(guid);

            await grain.DoSomething();

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数
            SetStoredValue<MockStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", 42);

            await grain.DoRead();
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(2, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（第 2 次检查）
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（第 2 次检查）

            Assert.Equal(42, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("MemoryStore")]
        public async Task MemoryStore_Read_Write()
        {
            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");
            IMemoryStorageTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IMemoryStorageTestGrain>(guid);

            int val = await grain.GetValue();

            Assert.Equal(0, val); // 初始值

            await grain.DoWrite(1);
            val = await grain.GetValue();
            Assert.Equal(1, val); // 第 1 次写入后的值

            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val); // 第 2 次写入后的值

            val = await grain.DoRead();

            Assert.Equal(2, val); // 重新读取后的值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("MemoryStore")]
        public async Task MemoryStore_Delete()
        {
            Guid id = Guid.NewGuid();
            var grain = this.HostedCluster.GrainFactory!.GetGrain<IMemoryStorageTestGrain>(id);
            await grain.DoWrite(1);
            await grain.DoDelete();
            int val = await grain.GetValue(); // 此处是否应改为抛出异常？
            Assert.Equal(0, val); // 删除后的值
            await grain.DoWrite(2);
            val = await grain.GetValue();
            Assert.Equal(2, val); // 删除后再次写入的值
        }

        [Fact, TestCategory("Stress"), TestCategory("CorePerf"), TestCategory("Persistence"), TestCategory("MemoryStore")]
        public async Task MemoryStore_Stress_Read()
        {
            const int numIterations = 10000;

            Stopwatch sw = Stopwatch.StartNew();

            Task<int>[] promises = new Task<int>[numIterations];
            for (int i = 0; i < numIterations; i++)
            {
                IMemoryStorageTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IMemoryStorageTestGrain>(Guid.NewGuid());
                int idx = i; // 捕获
                async Task<int> asyncFunc()
                {
                    await grain.DoWrite(idx);
                    return await grain.DoRead();
                }
                promises[i] = Task.Run(asyncFunc);
            }
            await Task.WhenAll(promises);

            TimeSpan elapsed = sw.Elapsed;
            double tps = (numIterations * 2) / elapsed.TotalSeconds; // 每次迭代执行一次读取和一次写入。
            output.WriteLine("{0} Completed Read-Write operations in {1} at {2} TPS", numIterations, elapsed, tps);

            for (int i = 0; i < numIterations; i++)
            {
                int expectedVal = i;
                Assert.Equal(expectedVal, await promises[i]);  //  "Returned value - Read @ #" + i
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Write()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);

            await grain.DoWrite(1);

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(1, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            await grain.DoWrite(2);
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(2, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(2, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Delete()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(id);

            await grain.DoWrite(1);

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            int initialReadCount = providerState.ProviderStateForTest.ReadCount;
            int initialWriteCount = providerState.ProviderStateForTest.WriteCount;
            int initialDeleteCount = providerState.ProviderStateForTest.DeleteCount;

            await grain.DoDelete();
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(initialDeleteCount + 1, providerState.ProviderStateForTest.DeleteCount); // StorageProvider 删除次数
            Assert.Null(providerState.LastStoredGrainState); // 存储在删除后为空。

            int val = await grain.GetValue(); // 不重新读取，直接返回当前内存中的 null 数据。
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName); // 更新状态。
            Assert.Equal(0, val); // 删除后的值
            Assert.Equal(initialReadCount, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数

            await grain.DoWrite(2);
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName); // 更新状态。
            Assert.Equal(initialReadCount, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(initialWriteCount + 1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(2, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Read_Error()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceErrorGrain>(id);
            _ = await grain.GetValue();

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            try
            {
                await grain.DoReadError(true);
            }
            catch (ApplicationException)
            {
                // 预期错误。
            }
            catch (AggregateException ae)
            {
                Exception e = ae.GetBaseException();
                if (e is ApplicationException)
                {
                    // 预期错误。
                }
                else
                {
                    throw e;
                }
            }

            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（原注释标签 -2）
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（原注释标签 -2）

            try
            {
                await grain.DoReadError(false);
            }
            catch (ApplicationException)
            {
                // 预期错误。
            }
            catch (AggregateException ae)
            {
                Exception e = ae.GetBaseException();
                if (e is ApplicationException)
                {
                    // 预期错误。
                }
                else
                {
                    throw e;
                }
            }
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(2, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（原注释标签 -2）
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（原注释标签 -2）
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_Write_Error()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceErrorGrain>(id);

            await grain.DoWrite(1);

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(1, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            await grain.DoWrite(2);
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(2, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(2, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            try
            {
                await grain.DoWriteError(3, true);
            }
            catch (ApplicationException)
            {
                // 预期错误。
            }
            catch (AggregateException ae)
            {
                if (ae.GetBaseException() is ApplicationException)
                {
                    // 预期错误。
                }
                else
                {
                    throw;
                }
            }
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName); // 刷新获取的提供程序状态。
            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(2, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(2, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            try
            {
                await grain.DoWriteError(4, false);
            }
            catch (ApplicationException)
            {
                // 预期错误。
            }
            catch (AggregateException ae)
            {
                if (ae.GetBaseException() is ApplicationException)
                {
                    // 预期错误。
                }
                else
                {
                    throw;
                }
            }
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(3, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            Assert.Equal(4, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_ReRead_Error()
        {
            const string providerName = "test1";

            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");
            IPersistenceErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceErrorGrain>(guid);
            _ = await grain.GetValue();

            var providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);

            Assert.Equal(1, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数

            SetStoredValue<MockStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", 42);

            await grain.DoRead();
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(2, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（原注释标签 -2）
            Assert.Equal(0, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（原注释标签 -2）

            Assert.Equal(42, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            await grain.DoWrite(43);
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(2, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（原注释标签 -2）
            Assert.Equal(1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（原注释标签 -2）

            Assert.Equal(43, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            try
            {
                await grain.DoReadError(true);
            }
            catch (ApplicationException)
            {
                // 预期错误。
            }
            catch (AggregateException ae)
            {
                if (ae.GetBaseException() is ApplicationException)
                {
                    // 预期错误。
                }
                else
                {
                    throw;
                }
            }
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(2, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（原注释标签 -2）
            Assert.Equal(1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（原注释标签 -2）

            Assert.Equal(43, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            try
            {
                await grain.DoReadError(false);
            }
            catch (ApplicationException)
            {
                // 预期错误。
            }
            catch (AggregateException ae)
            {
                if (ae.GetBaseException() is ApplicationException)
                {
                    // 预期错误。
                }
                else
                {
                    throw;
                }
            }
            providerState = GetStateForStorageProviderInUse<MockStorageProvider>(providerName);
            Assert.Equal(3, providerState.ProviderStateForTest.ReadCount); // StorageProvider 读取次数（原注释标签 -2）
            Assert.Equal(1, providerState.ProviderStateForTest.WriteCount); // StorageProvider 写入次数（原注释标签 -2）

            Assert.Equal(43, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Error_BeforeRead()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            string id = guid.ToString("N");
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(guid);

            var val = await grain.GetValue(); // 激活 Grain。
            int expectedVal = 42;

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);
            val = await grain.DoRead();

            Assert.Equal(expectedVal, val); // 返回值
            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeRead);

            await CheckStorageProviderErrors(grain.DoRead, TestContext.Current.CancellationToken);

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);

            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Error_AfterRead()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            string id = guid.ToString("N");
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(guid);

            var val = await grain.GetValue(); // 激活 Grain。
            int expectedVal = 52;

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);

            val = await grain.DoRead();

            Assert.Equal(expectedVal, val); // 返回值

            await SetErrorInjection(providerName, ErrorInjectionPoint.AfterRead);
            await CheckStorageProviderErrors(grain.DoRead, TestContext.Current.CancellationToken);

            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值

            int newVal = 53;
            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", newVal);

            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值

            await grain.DoRead(); // 强制重新读取。
            expectedVal = newVal;

            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Error_BeforeWrite()
        {
            Guid id = Guid.NewGuid();
            string providerName = ErrorInjectorProviderName;
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(id);

            var val = await grain.GetValue();

            int expectedVal = 62;
            await grain.DoWrite(expectedVal);
            var providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            const int attemptedVal3 = 63;
            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeWrite);
            await CheckStorageProviderErrors(() => grain.DoWrite(attemptedVal3), TestContext.Current.CancellationToken);

            // 已存储的值未改变。
            providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            val = await grain.GetValue();
            // 已存储的值未改变。
            providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
#if REREAD_STATE_AFTER_WRITE_FAILED
            Assert.Equal(expectedVal, val); // 最近一次成功写入的值。
#else
            Assert.Equal(attemptedVal3, val); // 最近一次尝试写入的值仍在内存中。
#endif
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_InconsistentStateException_DeactivatesGrain()
        {
            Guid id = Guid.NewGuid();
            string providerName = ErrorInjectorProviderName;
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(id);

            var val = await grain.GetValue();

            int expectedVal = 62;
            var originalActivationId = await grain.GetActivationId();
            await grain.DoWrite(expectedVal);
            var providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            const int attemptedVal3 = 63;
            await SetErrorInjection(providerName, new ErrorInjectionBehavior
            {
                ErrorInjectionPoint = ErrorInjectionPoint.BeforeWrite,
                ExceptionType = typeof(InconsistentStateException)
            });
            await CheckStorageProviderErrors(
                () => grain.DoWrite(attemptedVal3),
                TestContext.Current.CancellationToken,
                typeof(InconsistentStateException));

            // 已存储的值未改变。
            providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
            Assert.NotEqual(originalActivationId, await grain.GetActivationId());

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            val = await grain.GetValue();
            // 已存储的值未改变。
            providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            // 该值不应发生变化。
            Assert.Equal(expectedVal, val);
        }

        /// <summary>
        /// 测试 <see cref="InconsistentStateException"/> 导致的停用仅影响
        /// 抛出该异常的 Grain。
        /// </summary>
        /// <returns></returns>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_InconsistentStateException_DeactivatesOnlyCurrentGrain()
        {
            var target = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(Guid.NewGuid());
            var proxy = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorProxyGrain>(Guid.NewGuid());

            // 记录原始激活 ID。
            var targetActivationId = await target.GetActivationId();
            var proxyActivationId = await proxy.GetActivationId();

            // 触发状态不一致异常。
            await SetErrorInjection(ErrorInjectorProviderName, new ErrorInjectionBehavior
            {
                ErrorInjectionPoint = ErrorInjectionPoint.BeforeWrite,
                ExceptionType = typeof(InconsistentStateException)
            });
            await CheckStorageProviderErrors(
                () => proxy.DoWrite(63, target),
                TestContext.Current.CancellationToken,
                typeof(InconsistentStateException));

            // 目标应已因异常而停用。
            Assert.NotEqual(targetActivationId, await target.GetActivationId());

            // 调用目标 Grain 的 Grain 不应被停用。
            Assert.Equal(proxyActivationId, await proxy.GetActivationId());
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Error_AfterWrite()
        {
            Guid id = Guid.NewGuid();
            string providerName = ErrorInjectorProviderName;
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(id);

            var val = await grain.GetValue();

            int expectedVal = 82;
            await grain.DoWrite(expectedVal);
            var providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            const int attemptedVal4 = 83;
            await SetErrorInjection(providerName, ErrorInjectionPoint.AfterWrite);
            await CheckStorageProviderErrors(() => grain.DoWrite(attemptedVal4), TestContext.Current.CancellationToken);

            // 存储中的值已发生变化。
            expectedVal = attemptedVal4;
            providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Error_BeforeReRead()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            string id = guid.ToString("N");
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(guid);

            var val = await grain.GetValue();
            int expectedVal = 72;
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);
            val = await grain.DoRead();
            Assert.Equal(expectedVal, val); // 返回值

            expectedVal = 73;
            await grain.DoWrite(expectedVal);
            var providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeRead);
            await CheckStorageProviderErrors(grain.DoRead, TestContext.Current.CancellationToken);

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Error_AfterReRead()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            string id = guid.ToString("N");
            IPersistenceProviderErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceProviderErrorGrain>(guid);

            var val = await grain.GetValue();

            int expectedVal = 92;
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);
            val = await grain.DoRead();
            Assert.Equal(expectedVal, val); // 返回值

            expectedVal = 93;
            await grain.DoWrite(expectedVal);
            var providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1

            expectedVal = 94;
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);
            await SetErrorInjection(providerName, ErrorInjectionPoint.AfterRead);
            await CheckStorageProviderErrors(grain.DoRead, TestContext.Current.CancellationToken);

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Error_Handled_Read()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");
            IPersistenceUserHandledErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceUserHandledErrorGrain>(guid);
            _ = await grain.GetValue(); // 激活 Grain。
            int expectedVal = 42;

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);

            var val = await grain.DoRead(false);

            Assert.Equal(expectedVal, val); // 返回值

            int newVal = expectedVal + 1;

            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", newVal);
            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeRead);
            val = await grain.DoRead(true);
            Assert.Equal(expectedVal, val); // 返回值
            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            expectedVal = newVal;

            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", newVal);
            val = await grain.DoRead(false);
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Error_Handled_Write()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            _ = guid.ToString("N");
            IPersistenceUserHandledErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceUserHandledErrorGrain>(guid);
            _ = await grain.GetValue(); // 激活 Grain。
            int expectedVal = 42;

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);

            var val = await grain.DoRead(false);

            Assert.Equal(expectedVal, val); // 返回值

            int newVal = expectedVal + 1;
            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeWrite);
            await grain.DoWrite(newVal, true);
            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);

            expectedVal = newVal;
            await grain.DoWrite(newVal, false);
            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 返回值
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Error_NotHandled_Write()
        {
            string providerName = ErrorInjectorProviderName;
            Guid guid = Guid.NewGuid();
            string id = guid.ToString("N");
            IPersistenceUserHandledErrorGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceUserHandledErrorGrain>(guid);

            var val = await grain.GetValue(); // 激活 Grain。
            int expectedVal = 42;

            await SetErrorInjection(providerName, ErrorInjectionPoint.None);
            SetStoredValue<ErrorInjectionStorageProvider>(providerName, DefaultGrainStateName, grain, "Field1", expectedVal);
            val = await grain.DoRead(false);

            Assert.Equal(expectedVal, val); // 读取后的返回值

            int newVal = expectedVal + 1;
            await SetErrorInjection(providerName, ErrorInjectionPoint.BeforeWrite);
            await CheckStorageProviderErrors(() => grain.DoWrite(newVal, false), TestContext.Current.CancellationToken);

            val = await grain.GetValue();
            // 已存储的值未改变。
            var providerState = GetStateForStorageProviderInUse<ErrorInjectionStorageProvider>(providerName);
            Assert.Equal(expectedVal, providerState.LastStoredGrainState!.Field1); // 存储中的 Field1
#if REREAD_STATE_AFTER_WRITE_FAILED
            Assert.Equal(expectedVal, val); // 写入失败后：最近一次成功写入的值。
#else
            Assert.Equal(newVal, val); // 写入失败后：最近一次尝试写入的值仍在内存中。
#endif
            await SetErrorInjection(providerName, ErrorInjectionPoint.None);

            expectedVal = newVal;
            await grain.DoWrite(newVal, false);
            val = await grain.GetValue();
            Assert.Equal(expectedVal, val); // 成功写入后的返回值
        }

        [Fact, TestCategory("Stress"), TestCategory("CorePerf"), TestCategory("Persistence")]
        public async Task Persistence_Provider_Loop_Read()
        {
            const int numIterations = 100;

            Task<int>[] promises = new Task<int>[numIterations];
            for (int i = 0; i < numIterations; i++)
            {
                int expectedVal = i;
                IPersistenceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceTestGrain>(Guid.NewGuid());
                Guid guid = grain.GetPrimaryKey();
                _ = guid.ToString("N");

                SetStoredValue<MockStorageProvider>(MockStorageProviderName1, DefaultGrainStateName, grain, "Field1", expectedVal); // 在 Grain 不知情的情况下直接更新状态数据。
                promises[i] = grain.DoRead();
            }
            await Task.WhenAll(promises);

            for (int i = 0; i < numIterations; i++)
            {
                int expectedVal = i;
                Assert.Equal(expectedVal, await promises[i]);  //  "Returned value - Read @ #" + i
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_BadProvider()
        {
            IBadProviderTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IBadProviderTestGrain>(Guid.NewGuid());
            var oex = await Assert.ThrowsAsync<BadProviderConfigException>(() => grain.DoSomething());
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public void OrleansException_BadProvider()
        {
            string msg1 = "BadProvider";
            string msg2 = "Wrapper";
            string msg3 = "Aggregate";

            var bpce = new BadProviderConfigException(msg1);
            var oe = new OrleansException(msg2, bpce);
            var ae = new AggregateException(msg3, oe);

            Assert.NotNull(ae.InnerException); // AggregateException.InnerException 不应为 null。
            Assert.IsAssignableFrom<OrleansException>(ae.InnerException);
            Exception exc = ae.InnerException;
            Assert.NotNull(exc.InnerException); // OrleansException.InnerException 不应为 null。
            Assert.IsAssignableFrom<BadProviderConfigException>(exc.InnerException);

            exc = ae.GetBaseException();
            Assert.NotNull(exc.InnerException); // BaseException.InnerException 不应为 null。
            Assert.IsAssignableFrom<BadProviderConfigException>(exc.InnerException);

            Assert.StartsWith(msg3, ae.Message);  //  "AggregateException.Message should be '{0}'", msg3
            Assert.Equal(msg2, exc.Message);  //  "OrleansException.Message should be '{0}'", msg2
            Assert.Equal(msg1, exc.InnerException.Message);  //  "InnerException.Message should be '{0}'", msg1
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("MemoryStore")]
        public async Task MemoryStore_UserGrain_Read_Write()
        {
            Guid id = Guid.NewGuid();
            IUser grain = this.HostedCluster.GrainFactory!.GetGrain<IUser>(id);

            string name = id.ToString();

            await grain.SetName(name);

            string readName = await grain.GetName();

            Assert.Equal(name, readName); // 读回先前设置的名称。

            Guid id1 = Guid.NewGuid();
            Guid id2 = Guid.NewGuid();
            string name1 = id1.ToString();
            string name2 = id2.ToString();
            IUser friend1 = this.HostedCluster.GrainFactory!.GetGrain<IUser>(id1);
            IUser friend2 = this.HostedCluster.GrainFactory!.GetGrain<IUser>(id2);
            await friend1.SetName(name1);
            await friend2.SetName(name2);

            var readName1 = await friend1.GetName();
            var readName2 = await friend2.GetName();

            Assert.Equal(name1, readName1); // 第 1 位好友的姓名
            Assert.Equal(name2, readName2); // 第 2 位好友的姓名

            await grain.AddFriend(friend1);
            await grain.AddFriend(friend2);

            var friends = await grain.GetFriends();
            Assert.Equal(2, friends.Count); // 好友数量
            Assert.Equal(name1, await friends[0].GetName()); // GetFriends：第 1 位好友的姓名
            Assert.Equal(name2, await friends[1].GetName()); // GetFriends：第 2 位好友的姓名
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_Grain_NoState()
        {
            const string providerName = MockStorageProviderName1;
            Guid id = Guid.NewGuid();
            IPersistenceNoStateTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IPersistenceNoStateTestGrain>(id);

            await grain.DoSomething();

            Assert.True(HasStorageProvider(providerName));
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Serialization")]
        public void Serialize_GrainState_DeepCopy()
        {
            // 注意：此测试要求 Silo 正在运行且客户端已初始化，以便在序列化前解析 Grain 引用。
            IUser[] grains = new IUser[3];
            grains[0] = this.HostedCluster.GrainFactory!.GetGrain<IUser>(Guid.NewGuid());
            grains[1] = this.HostedCluster.GrainFactory!.GetGrain<IUser>(Guid.NewGuid());
            grains[2] = this.HostedCluster.GrainFactory!.GetGrain<IUser>(Guid.NewGuid());

            GrainStateContainingGrainReferences initialState = new GrainStateContainingGrainReferences();
            foreach (var g in grains)
            {
                initialState.GrainList.Add(g);
                initialState.GrainDict.Add(g.GetPrimaryKey().ToString(), g);
            }

            var copy = (GrainStateContainingGrainReferences)this.HostedCluster.DeepCopy(initialState)!;
            Assert.NotSame(initialState.GrainDict, copy.GrainDict); // 字典
            Assert.NotSame(initialState.GrainList, copy.GrainList); // 列表
        }

        [Fact, TestCategory("Persistence"), TestCategory("Serialization"), TestCategory("CorePerf"), TestCategory("Stress")]
        public async Task Serialize_GrainState_DeepCopy_Stress()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            int num = 100;
            int loops = num * 100;
            GrainStateContainingGrainReferences[] states = new GrainStateContainingGrainReferences[num];
            for (int i = 0; i < num; i++)
            {
                IUser grain = this.HostedCluster.GrainFactory!.GetGrain<IUser>(Guid.NewGuid());
                states[i] = new GrainStateContainingGrainReferences();
                states[i].GrainList.Add(grain);
                states[i].GrainDict.Add(grain.GetPrimaryKey().ToString(), grain);
            }

            List<Task> tasks = new List<Task>();
            for (int i = 0; i < loops; i++)
            {
                int idx = Random.Shared.Next(num);
                tasks.Add(Task.Run(() => { var copy = this.HostedCluster.DeepCopy(states[idx]); }, cancellationToken));
                tasks.Add(Task.Run(() => { var other = this.HostedCluster.RoundTripSerializationForTesting(states[idx]); }, cancellationToken));
            }
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);

            //Task copyTask = Task.Run(() =>
            //{
            //    for (int i = 0; i < loops; i++)
            //    {
            //        int idx = random.Next(num);
            //        var copy = states[idx].DeepCopy();
            //    }
            //});
            //Task serializeTask = Task.Run(() =>
            //{
            //    for (int i = 0; i < loops; i++)
            //    {
            //        int idx = random.Next(num);
            //        var other = SerializationManager.RoundTripSerializationForTesting(states[idx]);
            //    }
            //});
            //await Task.WhenAll(copyTask, serializeTask);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Scheduler"), TestCategory("Reentrancy")]
        public async Task ReentrentGrainWithState()
        {
            Guid id1 = Guid.NewGuid();
            Guid id2 = Guid.NewGuid();
            IReentrentGrainWithState grain1 = this.HostedCluster.GrainFactory!.GetGrain<IReentrentGrainWithState>(id1);
            IReentrentGrainWithState grain2 = this.HostedCluster.GrainFactory!.GetGrain<IReentrentGrainWithState>(id2);
            await Task.WhenAll(grain1.Setup(grain2), grain2.Setup(grain1));

            Task t11 = grain1.Test1();
            Task t12 = grain1.Test2();
            Task t21 = grain2.Test1();
            Task t22 = grain2.Test2();
            await Task.WhenAll(t11, t12, t21, t22);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Scheduler"), TestCategory("Reentrancy")]
        public async Task NonReentrentStressGrainWithoutState()
        {
            Guid id1 = Guid.NewGuid();
            INonReentrantStressGrainWithoutState grain1 = this.HostedCluster.GrainFactory!.GetGrain<INonReentrantStressGrainWithoutState>(id1);
            await grain1.Test1();
        }

        private const bool DoStart = true; // 除非值为 True，否则 Task.Delay 测试失败（超时）。

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Scheduler"), TestCategory("Reentrancy")]
        public async Task ReentrentGrain_Task_Delay()
        {
            Guid id1 = Guid.NewGuid();
            IReentrentGrainWithState grain1 = this.HostedCluster.GrainFactory!.GetGrain<IReentrentGrainWithState>(id1);

            await grain1.Task_Delay(DoStart);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Scheduler"), TestCategory("Reentrancy")]
        public async Task NonReentrentGrain_Task_Delay()
        {
            Guid id1 = Guid.NewGuid();
            INonReentrantStressGrainWithoutState grain1 = this.HostedCluster.GrainFactory!.GetGrain<INonReentrantStressGrainWithoutState>(id1);

            await grain1.Task_Delay(DoStart);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence"), TestCategory("Scheduler"), TestCategory("Reentrancy")]
        public async Task StateInheritanceTest()
        {
            Guid id1 = Guid.NewGuid();
            IStateInheritanceTestGrain grain = this.HostedCluster.GrainFactory!.GetGrain<IStateInheritanceTestGrain>(id1);

            await grain.SetValue(1);
            int val = await grain.GetValue();
            Assert.Equal(1, val);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task SurrogatePersistence_TypeWithoutPublicConstructor_Read()
        {
            ISurrogateStateForTypeWithoutPublicConstructorGrain<ExternalTypeWithoutPublicConstructor> grain = HostedCluster.GrainFactory!
                .GetGrain<ISurrogateStateForTypeWithoutPublicConstructorGrain<ExternalTypeWithoutPublicConstructor>>(Guid.NewGuid());
            ExternalTypeWithoutPublicConstructor instance = ExternalTypeWithoutPublicConstructor.Create(1, 2);

            await grain.SetState(instance);
            ExternalTypeWithoutPublicConstructor val = await grain.GetState();

            Assert.Equal(1, val.Field1);
            Assert.Equal(2, val.Field2);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [TestArea("Runtime")]
        [Fact, TestCategory("Functional"), TestCategory("Persistence")]
        public async Task Persistence_RecordTypeWithoutPublicParameterlessConstructor_Read()
        {
            IRecordTypeWithoutPublicParameterlessConstructorGrain<RecordTypeWithoutPublicParameterlessConstructor> grain = HostedCluster.GrainFactory!
                .GetGrain<IRecordTypeWithoutPublicParameterlessConstructorGrain<RecordTypeWithoutPublicParameterlessConstructor>>(Guid.NewGuid());
            RecordTypeWithoutPublicParameterlessConstructor instance = new RecordTypeWithoutPublicParameterlessConstructor(1);

            await grain.SetState(instance);
            RecordTypeWithoutPublicParameterlessConstructor val = await grain.GetState();

            Assert.Equal(1, val.Field);
        }

        // ---------- 辅助函数 ----------
        private void SetStoredValue<T>(string providerName, string grainType, IGrain grain, string fieldName, int newValue) where T : IControllable
        {
            IManagementGrain mgmtGrain = this.HostedCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            // 准备 SetVal 函数的参数。
            var args = new MockStorageProvider.SetValueArgs
            {
                Val = newValue,
                Name = "Field1",
                GrainType = grainType,
                GrainId = grain.GetGrainId(),
                StateType = typeof(PersistenceTestGrainState)
            };
            mgmtGrain.SendControlCommandToProvider<T>(
                providerName, (int)MockStorageProvider.Commands.SetValue, args).Wait();
        }

        private async Task SetErrorInjection(string providerName, ErrorInjectionPoint errorInjectionPoint)
        {
            await SetErrorInjection(providerName, new ErrorInjectionBehavior { ErrorInjectionPoint = errorInjectionPoint });
        }

        private async Task SetErrorInjection(string providerName, ErrorInjectionBehavior errorInjectionBehavior)
        {
            await ErrorInjectionStorageProvider.SetErrorInjection(providerName, errorInjectionBehavior, this.HostedCluster.GrainFactory!);
        }

        private async Task CheckStorageProviderErrors(
            Func<Task> taskFunc,
            CancellationToken cancellationToken,
            Type? expectedException = null)
        {
            StackTrace at = new StackTrace();
            TimeSpan timeout = Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15);
            try
            {
                await taskFunc().WaitAsync(timeout, cancellationToken);

                if (ErrorInjectionStorageProvider.DoInjectErrors)
                {
                    string msg = "StorageProviderInjectedError exception should have been thrown " + at;
                    output.WriteLine("Assertion failed: {0}", msg);
                    Assert.Fail(msg);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                output.WriteLine("Exception caught: {0}", e);
                var baseException = e.GetBaseException();
                if (baseException is OrleansException && baseException.InnerException != null)
                {
                    baseException = baseException.InnerException;
                }

                Assert.IsAssignableFrom(expectedException ?? typeof(StorageProviderInjectedError), baseException);
                //if (exc is StorageProviderInjectedError)
                //{
                //     //Expected error
                //}
                //else
                //{
                //    output.WriteLine("Unexpected exception: {0}", exc);
                //    Assert.True(false, exc.ToString());
                //}
            }
        }
        private bool HasStorageProvider(string providerName)
        {
            foreach (var siloHandle in this.HostedCluster.GetActiveSilos())
            {
                if (this.HostedCluster.Client!.GetTestHooks(siloHandle).HasStorageProvider(providerName).Result)
                {
                    return true;
                }
            }
            return false;
        }

        private ProviderState GetStateForStorageProviderInUse<T>(string providerName, bool okNull = false) where T : IControllable
        {
            ProviderState providerState = new ProviderState();
            IManagementGrain mgmtGrain = this.HostedCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            object?[] replies = mgmtGrain.SendControlCommandToProvider<T>(
               providerName, (int)MockStorageProvider.Commands.GetProvideState, null).Result;
            object?[] replies2 = mgmtGrain.SendControlCommandToProvider<T>(
                              providerName, (int)MockStorageProvider.Commands.GetLastState, null).Result;
            for (int i = 0; i < replies.Length; i++)
            {
                MockStorageProvider.StateForTest state = (MockStorageProvider.StateForTest)replies[i]!;
                PersistenceTestGrainState grainState = (PersistenceTestGrainState)replies2[i]!;
                if (state.ReadCount > 0)
                {
                    providerState.ProviderStateForTest = state;
                    providerState.LastStoredGrainState = grainState;
                    return providerState;
                }
            }

            return providerState;
        }

        private class ProviderState
        {
            public MockStorageProvider.StateForTest ProviderStateForTest { get; set; } = null!;
            public PersistenceTestGrainState? LastStoredGrainState { get; set; }
        }

        private void ResetMockStorageProvidersHistory()
        {
            var mockStorageProviders = new[] { MockStorageProviderName1, MockStorageProviderName2, MockStorageProviderNameLowerCase };
            foreach (var siloHandle in this.HostedCluster.GetActiveSilos().ToList())
            {
                foreach (var providerName in mockStorageProviders)
                {
                    if (!this.HostedCluster.Client!.GetTestHooks(siloHandle).HasStorageProvider(providerName).Result) continue;
                    IManagementGrain mgmtGrain = this.HostedCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
                    _ = mgmtGrain.SendControlCommandToProvider<MockStorageProvider>(
                       providerName, (int)MockStorageProvider.Commands.ResetHistory, null).Result;
                }
            }
        }
    }
}

// ReSharper restore RedundantAssignment
// ReSharper restore UnusedVariable
// ReSharper restore InconsistentNaming
