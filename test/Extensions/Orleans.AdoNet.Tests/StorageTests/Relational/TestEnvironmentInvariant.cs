using Newtonsoft.Json.Linq;
using Orleans.Tests.SqlUtils;
using Orleans.TestingHost.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using UnitTests.General;
using TestExtensions;

namespace UnitTests.StorageTests.Relational
{
    [Serializable]
    [DebuggerDisplay("StorageInvariant = {StorageInvariant}, ConnectionString = {ConnectionString}")]
    [Orleans.GenerateSerializer]
    public struct StorageConnection
    {
        [Orleans.Id(0)]
        public string StorageInvariant { get; set; }

        [Orleans.Id(1)]
        public string ConnectionString { get; set; }
    }

    [Serializable]
    [Orleans.GenerateSerializer]
    public class TestEnvironmentSettings
    {
        [Orleans.Id(0)]
        public ICollection<StorageConnection> ConnectionStrings { get; set; } = null!;

        [Orleans.Id(1)]
        public string EnvironmentId { get; set; } = null!;
    }

    /// <summary>
    /// 在开始运行测试之前，确保必要的环境不变条件成立。
    /// 这充当测试环境的类或对象不变条件。
    /// </summary>
    public class TestEnvironmentInvariant
    {
        /// <summary>
        /// 用于指定测试设置文件位置的环境变量。
        /// </summary>
        public const string EnvVariableForCustomSettingLocation = "customTestSettingsFileLocation";

        /// <summary>
        /// 未设置 <see cref="EnvVariableForCustomSettingLocation"/> 时，自定义设置文件的默认位置。
        /// </summary>
        public const string FallBackCustomTestSettingsFileLocation = @"..\..\..\CustomTestSettings.json";

        /// <summary>
        /// 与外部设置合并前的默认测试设置。
        /// </summary>
        public TestEnvironmentSettings DefaultSettings { get; } = new TestEnvironmentSettings
        {
            ConnectionStrings = new Collection<StorageConnection>(new List<StorageConnection>(new[]
            {
                new StorageConnection
                {
                    StorageInvariant = AdoNetInvariants.InvariantNameSqlServer,
                    ConnectionString = TestDefaultConfiguration.MsSqlConnectionString!
                },
                new StorageConnection
                {
                    StorageInvariant = AdoNetInvariants.InvariantNameMySql,
                    ConnectionString = TestDefaultConfiguration.MySqlConnectionString!
                },
                new StorageConnection
                {
                    StorageInvariant = AdoNetInvariants.InvariantNamePostgreSql,
                    ConnectionString = TestDefaultConfiguration.PostgresConnectionString!
                }
            })),
            EnvironmentId = "Default"
        };

        /// <summary>
        /// 默认设置与当前设置合并后的生效设置。
        /// </summary>
        public TestEnvironmentSettings ActiveSettings { get; set; }

        /// <summary>
        /// 默认构造函数。
        /// </summary>
        public TestEnvironmentInvariant()
        {
            ActiveSettings = TryLoadAndMergeWithCustomSettings(DefaultSettings);
        }

        /// <summary>
        /// 确保给定连接对应的存储可正常使用；若不可用，则尝试使其恢复可用。
        /// </summary>
        /// <param name="connection">用于确认存储可正常使用的连接。</param>
        /// <param name="storageName">存储名称，可选。</param>
        /// <returns></returns>
        public async Task<RelationalStorageForTesting?> EnsureStorageForTestingAsync(
            StorageConnection connection,
            string? storageName,
            CancellationToken cancellationToken)
        {
            if (AdoNetInvariants.Invariants.Contains(connection.StorageInvariant))
            {
                const string RelationalStorageTestDb = "OrleansStorageTests";
                return await RelationalStorageForTesting.SetupInstance(
                    connection.StorageInvariant,
                    storageName ?? RelationalStorageTestDb,
                    connectionString: connection.ConnectionString,
                    cancellationToken: cancellationToken);
            }

            return null;
        }

        /// <summary>
        /// 尝试确保测试开始前存储模拟器已运行。
        /// </summary>
        /// <remarks>此处或许可以扩展更多功能。</remarks>
        public bool EnsureEmulatorStorageForTesting()
        {
            return StorageEmulator.TryStart();
        }

        /// <summary>
        /// 尝试加载自定义设置；若找到，则尝试将其与给定的默认设置合并。
        /// </summary>
        /// <param name="defaultSettings">用于与自定义设置合并的默认设置。</param>
        /// <returns>合并后的设置。</returns>
        private static TestEnvironmentSettings TryLoadAndMergeWithCustomSettings(TestEnvironmentSettings defaultSettings)
        {
            string customTestSettingsFileLocation = Environment.GetEnvironmentVariable(EnvVariableForCustomSettingLocation, EnvironmentVariableTarget.User) ?? FallBackCustomTestSettingsFileLocation;

            var codeBaseUrl = new Uri(Assembly.GetExecutingAssembly().Location);
            var codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
            var dirPath = Path.GetDirectoryName(codeBasePath);
            var customFileLoc = Path.Combine(dirPath!, customTestSettingsFileLocation);

            var finalSettings = JObject.FromObject(defaultSettings);
            if (File.Exists(customFileLoc))
            {
                //TODO: 输出正在解析自定义值的提示……
                var mergeSettings = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union };
                var customSettingsJson = JObject.Parse(File.ReadAllText(customFileLoc));
                customSettingsJson.Merge(finalSettings, mergeSettings);
                finalSettings = customSettingsJson;
            }

            return finalSettings.ToObject<TestEnvironmentSettings>()!;
        }

        /// <summary>
        /// 检查给定存储是否可访问。
        /// </summary>
        /// <param name="connection">要检查的连接。</param>
        /// <returns></returns>
        private static async Task<bool> CanConnectToStorage(
            StorageConnection connection,
            CancellationToken cancellationToken)
        {
            //检测数据库能否连接出乎意料地棘手。可参考以下资料：
            //http://stackoverflow.com/questions/3668506/efficient-sql-test-query-or-validation-query-that-will-work-across-all-or-most.
            var storage = RelationalStorage.CreateInstance(connection.StorageInvariant, connection.ConnectionString);
            var query = connection.ConnectionString != AdoNetInvariants.InvariantNameOracleDatabase ? "SELECT 1;" : "SELECT 1 FROM DUAL;";
            try
            {
                await storage.ExecuteAsync(query, cancellationToken);
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
