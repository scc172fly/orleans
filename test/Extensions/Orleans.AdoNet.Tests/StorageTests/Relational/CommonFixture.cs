using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Tests.SqlUtils;
using Orleans.Storage;
using TestExtensions;
using UnitTests.General;
using Orleans.Serialization;
using Orleans.Serialization.Serializers;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 对所有类的共共定置件。 XUnit.NET 首先在测试运行期间保持它的生命
    /// 用于协调环境变化不定的强制执行和缓存
    /// 更重的设置。
    /// </summary>
    public class CommonFixture : TestEnvironmentFixture
    {
        /// <summary>
        /// 使用独特、后端特定密钥的多个用途的蓄水池存储提供者。
        /// 如果环境变量无法坚持, 数值将是 <em>nu</em>
        /// 建立储存提供者。
        /// </summary>
        private Dictionary<string, IGrainStorage?> StorageProviders { get; set; } = new Dictionary<string, IGrainStorage?>();

        /// <summary>
        /// 此功能用于锁定存储提供者字典 。
        /// </summary>
        private static AsyncLock StorageLock { get; } = new AsyncLock();

        /// <summary>
        /// 用于多种用途的电池 DefaultProviderRuntime 。
        /// </summary>
        private IProviderRuntime DefaultProviderRuntime { get; }

        /// <summary>
        /// 环境合同及其各种变化。
        /// </summary>
        private TestEnvironmentInvariant Invariants { get; } = new TestEnvironmentInvariant();

        /// <summary>
        /// 使用后的基本关系储存连接。
        /// </summary>
        public RelationalStorageForTesting? Storage { get; set; }

        /// <summary>
        /// 构造器
        /// </summary>
        public CommonFixture()
        {
            _ = this.Services.GetRequiredService<IOptions<ClusterOptions>>();
            DefaultProviderRuntime = new ClientProviderRuntime(
                this.InternalGrainFactory,
                this.Services,
                this.Services.GetRequiredService<ClientGrainContext>());
        }

        /// <summary>
        /// 根据环境变量返回对持久性提供者的正确执行。
        /// </summary>
        /// <remarks>如果环境变量未能维持储存提供者的创建,
        /// a <em>无效</em> 将提供数值。</remarks>
        public async Task<IGrainStorage?> GetStorageProvider(
            string storageInvariant,
            CancellationToken cancellationToken)
        {
            return await GetStorageProvider(storageInvariant, deleteStateOnClear: false, cancellationToken);
        }

        /// <summary>
        /// 根据环境变量返回对持久性提供者的正确执行。
        /// </summary>
        /// <param name="storageInvariant">存储提供者的 ADO.NET 变量名称。</param>
        /// <param name="deleteStateOnClear">如果 <see langword="true"/>,在清除状态时,提供者将从数据库中删除行。</param>
        /// <remarks>如果环境变量未能维持储存提供者的创建,
        /// a <em>无效</em> 将提供数值。</remarks>
        public async Task<IGrainStorage?> GetStorageProvider(
            string storageInvariant,
            bool deleteStateOnClear,
            CancellationToken cancellationToken)
        {
            //确保环境变异性在尝试给 SUT 功能即时之前保持稳健。
            //这样做,而不是建造者在如何启动环境方面拥有更多的颗粒。
            using (await StorageLock.LockAsync())
            {
                var cacheKey = deleteStateOnClear ? $"{storageInvariant}_DeleteOnClear" : storageInvariant;
                if (AdoNetInvariants.Invariants.Contains(storageInvariant))
                {
                    if (!StorageProviders.ContainsKey(cacheKey))
                    {
                        var connectionString = Invariants.ActiveSettings.ConnectionStrings.FirstOrDefault(i => i.StorageInvariant == storageInvariant);

                        if (string.IsNullOrWhiteSpace(connectionString.ConnectionString))
                        {
                            StorageProviders.Add(cacheKey, null);
                        }
                        else
                        {
                            // 每个存储模式都有自己的类固定, 因此它不能重建用于其他模式的数据库 。
                            var storageName = deleteStateOnClear ? "OrleansStorageTestsDeleteOnClear" : "OrleansStorageTests";
                            Storage = await Invariants.EnsureStorageForTestingAsync(
                                connectionString,
                                storageName,
                                cancellationToken);

                            var options = new AdoNetGrainStorageOptions()
                            {
                                ConnectionString = Storage!.Storage.ConnectionString,
                                Invariant = storageInvariant,
                                GrainStorageSerializer = new JsonGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<OrleansJsonSerializer>()!),
                                DeleteStateOnClear = deleteStateOnClear,
                            };
                            var clusterOptions = new ClusterOptions()
                            {
                                ServiceId = Guid.NewGuid().ToString()
                            };
                            var storageProvider = new AdoNetGrainStorage(
                                DefaultProviderRuntime.ServiceProvider.GetRequiredService<IActivatorProvider>(),
                                DefaultProviderRuntime.ServiceProvider.GetRequiredService<ILogger<AdoNetGrainStorage>>(),
                                Options.Create(options),
                                Options.Create(clusterOptions),
                                storageInvariant + "_StorageProvider");
                            ISiloLifecycleSubject siloLifeCycle = new SiloLifecycleSubject(NullLoggerFactory.Instance.CreateLogger<SiloLifecycleSubject>());
                            storageProvider.Participate(siloLifeCycle);
                            await siloLifeCycle.OnStart(cancellationToken);

                            StorageProviders[cacheKey] = storageProvider;
                        }
                    }
                }
            }

            var key = deleteStateOnClear ? $"{storageInvariant}_DeleteOnClear" : storageInvariant;
            return StorageProviders[key];
        }

        /// <summary>
        /// 创建一个不刻录的存储提供者, 使用给定的密钥格式和数据库上的恒定 hashher
        /// 由 <see cref="GetStorageProvider(string, bool, CancellationToken)"/>编制。
        /// </summary>
        public async Task<AdoNetGrainStorage> CreateConstantHashStorageProviderAsync(
            string storageInvariant,
            AdoNetGrainStorageKeyFormat keyFormat,
            CancellationToken cancellationToken)
        {
            var storage = Storage ?? throw new InvalidOperationException("The test database has not been prepared.");
            var options = new AdoNetGrainStorageOptions()
            {
                ConnectionString = storage.Storage.ConnectionString,
                Invariant = storageInvariant,
                GrainStorageSerializer = new JsonGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<OrleansJsonSerializer>()!),
                HashPicker = new StorageHasherPicker(new[] { new ConstantHasher() }),
                KeyFormat = keyFormat,
            };
            var storageProvider = new AdoNetGrainStorage(
                DefaultProviderRuntime.ServiceProvider.GetRequiredService<IActivatorProvider>(),
                DefaultProviderRuntime.ServiceProvider.GetRequiredService<ILogger<AdoNetGrainStorage>>(),
                Options.Create(options),
                Options.Create(new ClusterOptions { ServiceId = Guid.NewGuid().ToString() }),
                $"{storageInvariant}_{keyFormat}_StorageProvider");
            ISiloLifecycleSubject siloLifeCycle = new SiloLifecycleSubject(NullLoggerFactory.Instance.CreateLogger<SiloLifecycleSubject>());
            storageProvider.Participate(siloLifeCycle);
            await siloLifeCycle.OnStart(cancellationToken);
            return storageProvider;
        }

        /// <summary>
        /// 创建由调用器所准备的数据库的无缝存储提供者。
        /// </summary>
        public async Task<AdoNetGrainStorage> CreateStorageProviderAsync(
            string storageInvariant,
            string connectionString,
            string serviceId,
            bool deleteStateOnClear,
            CancellationToken cancellationToken)
        {
            var options = new AdoNetGrainStorageOptions()
            {
                ConnectionString = connectionString,
                Invariant = storageInvariant,
                GrainStorageSerializer = new JsonGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<OrleansJsonSerializer>()!),
                DeleteStateOnClear = deleteStateOnClear,
            };
            var storageProvider = new AdoNetGrainStorage(
                DefaultProviderRuntime.ServiceProvider.GetRequiredService<IActivatorProvider>(),
                DefaultProviderRuntime.ServiceProvider.GetRequiredService<ILogger<AdoNetGrainStorage>>(),
                Options.Create(options),
                Options.Create(new ClusterOptions { ServiceId = serviceId }),
                $"{storageInvariant}_Uncached_StorageProvider");
            ISiloLifecycleSubject siloLifeCycle = new SiloLifecycleSubject(NullLoggerFactory.Instance.CreateLogger<SiloLifecycleSubject>());
            storageProvider.Participate(siloLifeCycle);
            await siloLifeCycle.OnStart(cancellationToken);
            return storageProvider;
        }
    }
}
