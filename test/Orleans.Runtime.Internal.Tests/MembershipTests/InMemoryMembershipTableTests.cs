using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;
using Orleans.Serialization;
using Xunit;

namespace UnitTests.MembershipTests
{
    /// <summary>
    /// 发展集群所使用的模拟会籍表测试。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [TestCategory("BVT"), TestCategory("Membership")]
    public partial class InMemoryMembershipTableTests : IDisposable
    {
        private readonly InMemoryMembershipTable table;

        public InMemoryMembershipTableTests()
        {
            var services = new ServiceCollection();
            services.AddSerializer();
            var serviceProvider = _services = services.BuildServiceProvider();
            var deepCopier = serviceProvider.GetRequiredService<DeepCopier>();
            table = new InMemoryMembershipTable(deepCopier);
        }

        [Fact]
        public void CleanupDefunctSiloEntries_RemovesOnlyDeadOldEntries()
        {
            var tableVersion = table.ReadTableVersion();

            // 添加旧的死条目( 应删除)
            var deadEntry = CreateEntry(SiloStatus.Dead, daysOld: 10);
            Assert.True(table.Insert(deadEntry, tableVersion.Next()));
            tableVersion = table.ReadTableVersion();

            // 不死条目仍然是正式成员观点的一部分。
            var joiningEntry = CreateEntry(SiloStatus.Joining, daysOld: 10);
            Assert.True(table.Insert(joiningEntry, tableVersion.Next()));
            tableVersion = table.ReadTableVersion();

            // 添加旧的当前条目( 不应删除)
            var activeEntry = CreateEntry(SiloStatus.Active, daysOld: 10);
            Assert.True(table.Insert(activeEntry, tableVersion.Next()));
            tableVersion = table.ReadTableVersion();

            // 添加带有当前时间戳的新条目( 无论状态如何, 都不应该删除)
            var newEntry = CreateEntry(SiloStatus.Dead, daysOld: 0);
            Assert.True(table.Insert(newEntry, tableVersion.Next()));
            var beforeCleanup = table.ReadTableVersion();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-5);
            table.CleanupDefunctSiloEntries(cutoff);

            var data = table.ReadAll();
            Assert.Equal(beforeCleanup, data.Version);
            Assert.Equal(3, data.Members.Count);
            Assert.Contains(data.Members, m => m.Item1.SiloAddress.Equals(activeEntry.SiloAddress));
            Assert.Contains(data.Members, m => m.Item1.SiloAddress.Equals(newEntry.SiloAddress));
            Assert.DoesNotContain(data.Members, m => m.Item1.SiloAddress.Equals(deadEntry.SiloAddress));
            Assert.Contains(data.Members, m => m.Item1.SiloAddress.Equals(joiningEntry.SiloAddress));
        }

        [Fact]
        public void CleanupDefunctSiloEntries_PreservesEveryNonDeadStatus()
        {
            foreach (var status in Enum.GetValues<SiloStatus>().Where(status => status != SiloStatus.None))
            {
                var entry = CreateEntry(status, daysOld: 10);
                Assert.True(table.Insert(entry, table.ReadTableVersion().Next()));
            }

            var beforeCleanup = table.ReadTableVersion();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-5);
            table.CleanupDefunctSiloEntries(cutoff);

            var data = table.ReadAll();
            Assert.Equal(beforeCleanup, data.Version);
            Assert.Equal(
                Enum.GetValues<SiloStatus>().Where(status => status is not SiloStatus.None and not SiloStatus.Dead).Order(),
                data.Members.Select(row => row.Item1.Status).Order());
            table.CleanupDefunctSiloEntries(cutoff);
            Assert.Equal(data.Version, table.ReadTableVersion());
        }

        [Fact]
        public void CleanupDefunctSiloEntries_PreservesActiveEntries()
        {
            var tableVersion = table.ReadTableVersion();
            var activeEntry = CreateEntry(SiloStatus.Active, daysOld: 30);
            Assert.True(table.Insert(activeEntry, tableVersion.Next()));
            var beforeCleanup = table.ReadAll();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-5);
            table.CleanupDefunctSiloEntries(cutoff);

            var data = table.ReadAll();
            Assert.Single(data.Members);
            Assert.Equal(beforeCleanup.Version, data.Version);
            Assert.Equal(Assert.Single(beforeCleanup.Members).Item2, Assert.Single(data.Members).Item2);
            Assert.Equal(activeEntry.SiloAddress, data.Members[0].Item1.SiloAddress);
        }

        private static int _portCounter = 10000;

        private static MembershipEntry CreateEntry(SiloStatus status, int daysOld)
        {
            var port = Interlocked.Increment(ref _portCounter);
            var siloAddress = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, port), 0);
            var now = DateTime.UtcNow.AddDays(-daysOld);
            return new MembershipEntry
            {
                SiloAddress = siloAddress,
                HostName = "localhost",
                SiloName = $"TestSilo-{port}",
                Status = status,
                StartTime = now,
                IAmAliveTime = now,
            };
        }
    }
}
