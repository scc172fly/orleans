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
    /// 所有测试类共用的测试夹具。XUnit.NET 在首次实例化后会使其在测试运行期间保持存活，
    /// 因此用它来协调环境不变条件的检查，并缓存
    /// 开销较大的初始化结果。
    /// </summary>
    public class CommonFixture : TestEnvironmentFixture
    {
        /// <summary>
        /// 使用后端专用的唯一键缓存存储提供程序，以供多次使用。
        /// 该值为 <em>null</em> 的情形是，环境不变条件在
        /// 创建存储提供程序时未能成立。
        /// </summary>
        private Dictionary<string, IGrainStorage?> StorageProviders { get; set; } = new Dictionary<string, IGrainStorage?>();

        /// <summary>
        /// 用于锁定存储提供程序字典。
        /// </summary>
        private static AsyncLock StorageLock { get; } = new AsyncLock();

        /// <summary>
        /// 缓存 DefaultProviderRuntime 以供多次使用。
        /// </summary>
        private IProviderRuntime DefaultProviderRuntime { get; }

        /// <summary>
        /// 环境契约及其不变条件。
        /// </summary>
        private TestEnvironmentInvariant Invariants { get; } = new TestEnvironmentInvariant();

        /// <summary>
        /// 使用时的底层关系型存储连接。
        /// </summary>
        public RelationalStorageForTesting? Storage { get; set; }

        /// <summary>
        /// 构造函数。
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
        /// 根据环境变量返回正确的持久化提供程序实现。
        /// </summary>
        /// <remarks>如果创建存储提供程序时环境不变条件不成立，
        /// 则返回 <em>null</em>。</remarks>
        public async Task<IGrainStorage?> GetStorageProvider(
            string storageInvariant,
            CancellationToken cancellationToken)
        {
            return await GetStorageProvider(storageInvariant, deleteStateOnClear: false, cancellationToken);
        }

        /// <summary>
        /// 根据环境变量返回正确的持久化提供程序实现。
        /// </summary>
        /// <param name="storageInvariant">存储提供程序的 ADO.NET 固定名称（invariant name）。</param>
        /// <param name="deleteStateOnClear">若为 <see langword="true"/>，提供程序会在清除状态时从数据库中删除对应行。</param>
        /// <remarks>如果创建存储提供程序时环境不变条件不成立，
        /// 则返回 <em>null</em>。</remarks>
        public async Task<IGrainStorage?> GetStorageProvider(
            string storageInvariant,
            bool deleteStateOnClear,
            CancellationToken cancellationToken)
        {
            //在尝试提供可正常工作的被测系统（SUT）实例之前，确保环境不变条件成立。
            //不在构造函数中执行此操作，以便更细粒度地控制环境初始化方式。
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
                            // 每种存储模式都有各自的类级测试夹具，因此不得重新创建另一种模式正在使用的数据库。
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
        /// 使用给定的键格式和固定值哈希器，在以下方法准备的数据库上创建未缓存的存储提供程序：
        /// <see cref="GetStorageProvider(string, bool, CancellationToken)"/>。
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
        /// 在调用方准备的数据库上创建未缓存的存储提供程序。
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
