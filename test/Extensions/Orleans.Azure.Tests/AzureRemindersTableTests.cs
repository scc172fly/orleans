using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime.ReminderService;
using Tester;
using TestExtensions;
using Xunit;
using Orleans.Reminders.AzureStorage;
using Tester.AzureUtils;

namespace UnitTests.RemindersTest
{
    /// <summary>
    /// 使用 Azure 测试 Orleans 提醒器表的操作。
    /// </summary>
    [TestCategory("Reminders"), TestCategory("AzureStorage")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Reminders")]
    public class AzureRemindersTableTests : ReminderTableTestsBase
    {
        public AzureRemindersTableTests(ConnectionStringFixture fixture, TestEnvironmentFixture environment) : base(fixture, environment, CreateFilters())
        {
            TestUtils.CheckForAzureStorage();
        }

        private static LoggerFilterOptions CreateFilters()
        {
            var filters = new LoggerFilterOptions();
            filters.AddFilter("AzureTableDataManager", LogLevel.Trace);
            filters.AddFilter("OrleansSiloInstanceManager", LogLevel.Trace);
            filters.AddFilter("Storage", LogLevel.Trace);
            return filters;
        }

        public override ValueTask DisposeAsync()
        {
            // 测试结束后重置初始化超时
            return base.DisposeAsync();
        }

        protected override IReminderTable CreateRemindersTable()
        {
            TestUtils.CheckForAzureStorage();
            var options = Options.Create(new AzureTableReminderStorageOptions());
            options.Value.ConfigureTestDefaults();
            return new AzureBasedReminderTable(loggerFactory, this.clusterOptions, options);
        }

        protected override Task<string> GetConnectionString()
        {
            TestUtils.CheckForAzureStorage();
            return Task.FromResult("not used");
        }

        [Fact, TestCategory("Functional")]
        public void RemindersTable_Azure_Init()
        {
        }

        [Fact, TestCategory("Functional")]
        public async Task RemindersTable_Azure_RemindersRange()
        {
            await RemindersRange(50);
        }

        [Fact, TestCategory("Functional")]
        public async Task RemindersTable_Azure_RemindersParallelUpsert()
        {
            await RemindersParallelUpsert();
        }

        [Fact, TestCategory("Functional")]
        public async Task RemindersTable_Azure_ReminderSimple()
        {
            await ReminderSimple();
        }
    }
}
