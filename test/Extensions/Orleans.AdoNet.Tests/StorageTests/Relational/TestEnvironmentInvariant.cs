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
    /// 这强制规定在开始测试之前必须维持必要的环境。
    /// 此服务器是测试环境的类或对象变量 。
    /// </summary>
    public class TestEnvironmentInvariant
    {
        /// <summary>
        /// 设置测试设置文件位置的环境变量。
        /// </summary>
        public const string EnvVariableForCustomSettingLocation = "customTestSettingsFileLocation";

        /// <summary>
        /// 如果 <see cref="EnvVariableForCustomSettingLocation"/> 未被设置, 默认的自定义文件位置 。
        /// </summary>
        public const string FallBackCustomTestSettingsFileLocation = @"..\..\..\CustomTestSettings.json";

        /// <summary>
        /// 与外部组合前的默认测试设置。
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
        /// 将默认设置与活动设置合并后的活动设置。
        /// </summary>
        public TestEnvironmentSettings ActiveSettings { get; set; }

        /// <summary>
        /// 默认构建器 。
        /// </summary>
        public TestEnvironmentInvariant()
        {
            ActiveSettings = TryLoadAndMergeWithCustomSettings(DefaultSettings);
        }

        /// <summary>
        /// 确保与特定连接的存储功能正常,如果无法保存,则设法使其功能正常。
        /// </summary>
        /// <param name="connection">确保储存功能的连接。</param>
        /// <param name="storageName">存储名称。 这是可选的 。</param>
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
        /// 尝试确保存储模拟器在测试开始前运行。
        /// </summary>
        /// <remarks>这也许可以有更多的功能。</remarks>
        public bool EnsureEmulatorStorageForTesting()
        {
            return StorageEmulator.TryStart();
        }

        /// <summary>
        /// 尝试装入自定义设置, 如果找到的话, 尝试将其合并到给定的默认设置中 。
        /// </summary>
        /// <param name="defaultSettings">合并这些设置的默认设置。</param>
        /// <returns>合并后的结果设置。</returns>
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
                //TODO: 打印正在解析自定义值的文字...
                var mergeSettings = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Union };
                var customSettingsJson = JObject.Parse(File.ReadAllText(customFileLoc));
                customSettingsJson.Merge(finalSettings, mergeSettings);
                finalSettings = customSettingsJson;
            }

            return finalSettings.ToObject<TestEnvironmentSettings>()!;
        }

        /// <summary>
        /// 检查是否可到达给定的存储 。
        /// </summary>
        /// <param name="connection">连接要检查。</param>
        /// <returns></returns>
        private static async Task<bool> CanConnectToStorage(
            StorageConnection connection,
            CancellationToken cancellationToken)
        {
            //如何探测数据库连接到一个数据库的可连接性是令人惊讶的难题。
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
