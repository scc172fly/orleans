using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Orleans.TestingHost;

namespace TestExtensions
{
    public class TestDefaultConfiguration
    {
        private static readonly object LockObject = new object();
        private static IConfiguration defaultConfiguration = null!;

        static TestDefaultConfiguration()
        {
            InitializeDefaults();
        }

        public static void InitializeDefaults()
        {
            lock (LockObject)
            {
                defaultConfiguration = BuildDefaultConfiguration();
            }
        }

        public static bool UseAadAuthentication
        {
            get
            {
                bool.TryParse(defaultConfiguration[nameof(UseAadAuthentication)], out var value);
                return value;
            }
        }

        public static string? CosmosDBAccountEndpoint => defaultConfiguration[nameof(CosmosDBAccountEndpoint)];
        public static string? CosmosDBAccountKey => defaultConfiguration[nameof(CosmosDBAccountKey)];
        public static Uri TableEndpoint => new Uri(defaultConfiguration[nameof(TableEndpoint)]!);
        public static Uri DataBlobUri => new Uri(defaultConfiguration[nameof(DataBlobUri)]!);
        public static Uri DataQueueUri => new Uri(defaultConfiguration[nameof(DataQueueUri)]!);
        public static string? DataConnectionString => defaultConfiguration[nameof(DataConnectionString)];
        public static string? EventHubConnectionString => defaultConfiguration[nameof(EventHubConnectionString)];
        public static string? EventHubFullyQualifiedNamespace => defaultConfiguration[nameof(EventHubFullyQualifiedNamespace)];
        public static string? ZooKeeperConnectionString => defaultConfiguration[nameof(ZooKeeperConnectionString)];
        public static string? ConsulConnectionString => defaultConfiguration[nameof(ConsulConnectionString)];
        public static string? RedisConnectionString => defaultConfiguration[nameof(RedisConnectionString)];
        public static string? PostgresConnectionString => defaultConfiguration[nameof(PostgresConnectionString)];
        public static string? MySqlConnectionString => defaultConfiguration[nameof(MySqlConnectionString)];
        public static string? MsSqlConnectionString => defaultConfiguration[nameof(MsSqlConnectionString)];
        public static string? DynamoDbService => defaultConfiguration[nameof(DynamoDbService)];
        public static string? DynamoDbAccessKey => defaultConfiguration[nameof(DynamoDbAccessKey)];
        public static string? DynamoDbSecretKey => defaultConfiguration[nameof(DynamoDbSecretKey)];
        public static string? KinesisConnectionString => defaultConfiguration[nameof(KinesisConnectionString)];
        public static string? SqsConnectionString => defaultConfiguration[nameof(SqsConnectionString)];
        public static TokenCredential TokenCredential
        {
            get
            {
                var systemAccessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
                if (!string.IsNullOrEmpty(systemAccessToken))
                {
                    // 如果在 Azure DevOps 管道中运行且 SYSTEM_ACCESSTOKEN 可用，则尝试使用 AzurePipelinesCredential。
                    var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
                    var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
                    var serviceConnectionId = Environment.GetEnvironmentVariable("SERVICE_CONNECTION_ID");
                    return new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
                }
                return new DefaultAzureCredential();
            }
        }

        public static bool GetValue(string key, [NotNullWhen(true)] out string? value)
        {
            value = defaultConfiguration.GetValue(key, default(string));

            return value != null;
        }

        private static IConfiguration BuildDefaultConfiguration()
        {
            var builder = new ConfigurationBuilder();
            ConfigureHostConfiguration(builder);

            var config = builder.Build();
            return config;
        }

        public static void ConfigureHostConfiguration(IConfigurationBuilder builder)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { nameof(ZooKeeperConnectionString), "127.0.0.1:2181" }
            });
            if (!TryAddJsonFileFromEnvironmentVariable(builder, "ORLEANS_SECRETFILE"))
            {
                TryAddJsonFileInAncestorFolder(builder, "OrleansTestSecrets.json");
            }
            builder.AddEnvironmentVariables("Orleans");
        }

        /// <summary>
        /// 变通处理，使 PhysicalFileProvider 可以使用 JSON 序列化。
        /// </summary>
        private class SerializablePhysicalFileProvider : IFileProvider
        {
            [NonSerialized]
            private PhysicalFileProvider? fileProvider;

            public string? Root { get; set; }

            public IDirectoryContents GetDirectoryContents(string subpath)
            {
                return this.FileProvider().GetDirectoryContents(subpath);
            }

            public IFileInfo GetFileInfo(string subpath)
            {
                return this.FileProvider().GetFileInfo(subpath);
            }

            public IChangeToken Watch(string filter)
            {
                return this.FileProvider().Watch(filter);
            }

            private PhysicalFileProvider FileProvider()
            {
                return this.fileProvider ?? (this.fileProvider = new PhysicalFileProvider(this.Root!));
            }
        }

        /// <summary>尝试沿目录层次向上查找指定名称的文件，因为一些 CI 环境采用这种配置方式。</summary>
        private static bool TryAddJsonFileInAncestorFolder(IConfigurationBuilder builder, string fileName)
        {
            // 不过可能还有其他现成的实现方式。
            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (currentDir != null && currentDir.Exists)
            {
                string filePath = Path.Combine(currentDir.FullName, fileName);
                if (File.Exists(filePath))
                {
                    builder.AddJsonFile(new SerializablePhysicalFileProvider { Root = currentDir.FullName }, fileName, false, false);
                    return true;
                }

                currentDir = currentDir.Parent;
            }
            return false;
        }

        private static bool TryAddJsonFileFromEnvironmentVariable(IConfigurationBuilder builder, string envName)
        {
            var path = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                builder.AddJsonFile(new SerializablePhysicalFileProvider { Root = Path.GetDirectoryName(path) }, Path.GetFileName(path), false, false);
            }
            return false;
        }

        public static void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.ConfigureHostConfiguration(ConfigureHostConfiguration);
        }
    }
}
