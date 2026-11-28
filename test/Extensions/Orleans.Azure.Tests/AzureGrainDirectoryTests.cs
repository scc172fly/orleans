#nullable enable
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory.AzureStorage;
using Orleans.TestingHost.Utils;
using Tester.Directories;
using Xunit;

namespace Tester.AzureUtils
{
    [TestCategory("AzureStorage"), TestCategory("BVT")]
    [TestSuite("BVT")]
    [TestProvider("AzureStorage")]
    [TestArea("Membership")]
    public class AzureTableGrainDirectoryContractTests
    {
        [Fact]
        public async Task Register_NullAddress_ThrowsBeforeStorageAccess()
        {
            var directory = CreateGrainDirectory();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => directory.Register(null!));

            Assert.Equal("address", exception.ParamName);
        }

        [Fact]
        public async Task Register_NullAddressWithPreviousRegistration_ThrowsBeforeStorageAccess()
        {
            var directory = CreateGrainDirectory();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => directory.Register(null!, CreateGrainAddress()));

            Assert.Equal("address", exception.ParamName);
        }

        [Fact]
        public async Task Unregister_NullAddress_ThrowsBeforeStorageAccess()
        {
            var directory = CreateGrainDirectory();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => directory.Unregister(null!));

            Assert.Equal("address", exception.ParamName);
        }

        [Fact]
        public async Task UnregisterMany_NullAddresses_ThrowsBeforeStorageAccess()
        {
            var directory = CreateGrainDirectory();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => directory.UnregisterMany(null!));

            Assert.Equal("addresses", exception.ParamName);
        }

        [Fact]
        public async Task UnregisterMany_NullAddressElement_ThrowsBeforeStorageAccess()
        {
            var directory = CreateGrainDirectory();
            var addresses = new List<GrainAddress> { CreateGrainAddress(), null! };

            var exception = await Assert.ThrowsAsync<ArgumentException>(() => directory.UnregisterMany(addresses));

            Assert.Equal("addresses", exception.ParamName);
        }

        [Fact]
        public void Participate_ValidLifecycle_RegistersRuntimeInitialization()
        {
            var directory = CreateGrainDirectory();
            var lifecycle = new TestSiloLifecycle();

            directory.Participate(lifecycle);

            Assert.Equal(nameof(AzureTableGrainDirectory), lifecycle.ObserverName);
            Assert.Equal(ServiceLifecycleStage.RuntimeInitialize, lifecycle.Stage);
            Assert.NotNull(lifecycle.Observer);
        }

        [Fact]
        public void GrainDirectoryEntity_GrainAddress_RoundTrips()
        {
            var address = CreateGrainAddress();

            var entity = AzureTableGrainDirectory.GrainDirectoryEntity.FromGrainAddress("cluster-id", address);

            Assert.Equal(address, entity.ToGrainAddress());
        }

        private static AzureTableGrainDirectory CreateGrainDirectory() =>
            new(
                new AzureTableGrainDirectoryOptions(),
                Options.Create(new ClusterOptions { ClusterId = "cluster-id", ServiceId = "service-id" }),
                NullLoggerFactory.Instance);

        private static GrainAddress CreateGrainAddress() =>
            new()
            {
                ActivationId = ActivationId.NewId(),
                GrainId = GrainId.Parse("user/test"),
                SiloAddress = SiloAddress.FromParsableString("127.0.0.1:11111@1"),
                MembershipVersion = new MembershipVersion(1),
            };

        private sealed class TestSiloLifecycle : ISiloLifecycle
        {
            public int HighestCompletedStage => default;

            public int LowestStoppedStage => default;

            public string? ObserverName { get; private set; }

            public int Stage { get; private set; }

            public ILifecycleObserver? Observer { get; private set; }

            public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
            {
                ObserverName = observerName;
                Stage = stage;
                Observer = observer;
                return NullDisposable.Instance;
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static NullDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// 测试基于 Azure Table 的 Grain 目录功能，包括注册、查找和注销操作。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Directory")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Membership")]
    public class AzureTableGrainDirectoryTests(ITestOutputHelper testOutput) : GrainDirectoryTests<AzureTableGrainDirectory>(testOutput)
    {
        protected override AzureTableGrainDirectory CreateGrainDirectory()
        {
            TestUtils.CheckForAzureStorage();
            StorageEmulatorUtilities.EnsureEmulatorIsNotUsed();

            var clusterOptions = new ClusterOptions
            {
                ClusterId = Guid.NewGuid().ToString("N"),
                ServiceId = Guid.NewGuid().ToString("N"),
            };

            var directoryOptions = new AzureTableGrainDirectoryOptions();
            directoryOptions.ConfigureTestDefaults();

            var loggerFactory = TestingUtils.CreateDefaultLoggerFactory("AzureGrainDirectoryTests.log");

            var directory = new AzureTableGrainDirectory(directoryOptions, Options.Create(clusterOptions), loggerFactory);
            directory.InitializeIfNeeded().GetAwaiter().GetResult();

            return directory;
        }

        /// <summary>
        /// 测试批量注销多个 Grain 地址，包括处理并发修改的情况。
        /// </summary>
        [Fact]
        public async Task UnregisterMany()
        {
            const int N = 25;
            const int R = 4;

            // 创建并插入 N 个条目
            var addresses = new List<GrainAddress>();
            for (var i = 0; i < N; i++)
            {
                var addr = new GrainAddress
                {
                    ActivationId = ActivationId.NewId(),
                    GrainId = GrainId.Parse("user/someraondomuser_" + Guid.NewGuid().ToString("N")),
                    SiloAddress = SiloAddress.FromParsableString("10.0.23.12:1000@5678"),
                    MembershipVersion = new MembershipVersion(51)
                };
                addresses.Add(addr);
                await GrainDirectory.Register(addr, previousAddress: null);
            }

            // 在本地修改第 R 个条目，以模拟另一个 Silo 的激活尝试
            var ra = addresses[R];
            var oldActivation = ra.ActivationId;
            addresses[R] = new()
            {
                GrainId = ra.GrainId,
                SiloAddress = ra.SiloAddress,
                MembershipVersion = ra.MembershipVersion,
                ActivationId = ActivationId.NewId()
            };

            // 批量注销
            await GrainDirectory.UnregisterMany(addresses);

            // 现在应该只能找到原来的第 R 个条目
            for (int i = 0; i < N; i++)
            {
                if (i == R)
                {
                    var addr = await GrainDirectory.Lookup(addresses[i].GrainId);
                    Assert.NotNull(addr);
                    Assert.Equal(oldActivation, addr.ActivationId);
                }
                else
                {
                    Assert.Null(await GrainDirectory.Lookup(addresses[i].GrainId));
                }
            }
        }
    }
}
