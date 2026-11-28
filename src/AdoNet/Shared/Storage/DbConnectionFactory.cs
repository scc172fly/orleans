using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;

#if CLUSTERING_ADONET
namespace Orleans.Clustering.AdoNet.Storage
#elif PERSISTENCE_ADONET
namespace Orleans.Persistence.AdoNet.Storage
#elif REMINDERS_ADONET
namespace Orleans.Reminders.AdoNet.Storage
#elif STREAMING_ADONET
namespace Orleans.Streaming.AdoNet.Storage
#elif GRAINDIRECTORY_ADONET
namespace Orleans.GrainDirectory.AdoNet.Storage
#elif TESTER_SQLUTILS
namespace Orleans.Tests.SqlUtils
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// 此类缓存对所有已加载工厂的引用。
    internal static class DbConnectionFactory
    {
        private static readonly ConcurrentDictionary<string, CachedFactory> factoryCache =
            new ConcurrentDictionary<string, CachedFactory>();

        private static readonly Dictionary<string, List<Tuple<string, string>>> providerFactoryTypeMap =
            new Dictionary<string, List<Tuple<string, string>>>
            {
                { AdoNetInvariants.InvariantNameSqlServer, new List<Tuple<string, string>>{ new Tuple<string, string>("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient.SqlClientFactory") } },
                { AdoNetInvariants.InvariantNameMySql, new List<Tuple<string, string>>{ new Tuple<string, string>("MySql.Data", "MySql.Data.MySqlClient.MySqlClientFactory") } },
                { AdoNetInvariants.InvariantNameOracleDatabase, new List<Tuple<string, string>>{ new Tuple<string, string>("Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Client.OracleClientFactory") } },
                { AdoNetInvariants.InvariantNamePostgreSql, new List<Tuple<string, string>>{ new Tuple<string, string>("Npgsql", "Npgsql.NpgsqlFactory") } },
                { AdoNetInvariants.InvariantNameSqlLite, new List<Tuple<string, string>>{ new Tuple<string, string>("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite.SqliteFactory") } },
                { AdoNetInvariants.InvariantNameMySqlConnector, new List<Tuple<string, string>>{ new Tuple<string, string>("MySqlConnector", "MySqlConnector.MySqlConnectorFactory") , new Tuple<string, string>("MySqlConnector", "MySql.Data.MySqlClient.MySqlClientFactory") } },
            };

        private static CachedFactory GetFactory(string invariantName)
        {
            if (string.IsNullOrWhiteSpace(invariantName))
            {
                throw new ArgumentNullException(nameof(invariantName));
            }

            List<Tuple<string, string>>? providerFactoryDefinitions;
            if (!providerFactoryTypeMap.TryGetValue(invariantName, out providerFactoryDefinitions) || providerFactoryDefinitions.Count == 0)
                throw new InvalidOperationException($"Database provider factory with '{invariantName}' invariant name not supported.");

            List<Exception>? exceptions = null;
            foreach (var providerFactoryDefinition in providerFactoryDefinitions)
            {
                Assembly? asm = null;
                try
                {
                    var asmName = new AssemblyName(providerFactoryDefinition.Item1);
                    asm = Assembly.Load(asmName);
                }
                catch (Exception exc)
                {
                    AddException(new InvalidOperationException($"Unable to find and/or load a candidate assembly '{providerFactoryDefinition.Item1}' for '{invariantName}' invariant name.", exc));
                    continue;
                }

                if (asm == null)
                {
                    AddException(new InvalidOperationException($"Can't find database provider factory with '{invariantName}' invariant name. Please make sure that your ADO.Net provider package library is deployed with your application."));
                    continue;
                }

                var providerFactoryType = asm.GetType(providerFactoryDefinition.Item2);
                if (providerFactoryType == null)
                {
                    AddException(new InvalidOperationException($"Unable to load type '{providerFactoryDefinition.Item2}' for '{invariantName}' invariant name."));
                    continue;
                }

                var prop = providerFactoryType.GetFields().SingleOrDefault(p => string.Equals(p.Name, "Instance", StringComparison.OrdinalIgnoreCase) && p.IsStatic);
                if (prop == null)
                {
                    AddException(new InvalidOperationException($"Invalid provider type '{providerFactoryDefinition.Item2}' for '{invariantName}' invariant name."));
                    continue;
                }

                var factory = (DbProviderFactory?)prop.GetValue(null);
                return new CachedFactory(factory!, providerFactoryType.Name, "", providerFactoryType.AssemblyQualifiedName!);
            }

            throw new AggregateException(exceptions!);

            void AddException(Exception ex)
            {
                if (exceptions == null)
                {
                    exceptions = new List<Exception>();
                }
                exceptions.Add(ex);
            }
        }

        public static DbConnection CreateConnection(string invariantName, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(invariantName))
            {
                throw new ArgumentNullException(nameof(invariantName));
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            var factory = factoryCache.GetOrAdd(invariantName, GetFactory).Factory;
            var connection = factory.CreateConnection();

            if (connection == null)
            {
                throw new InvalidOperationException($"Database provider factory: '{invariantName}' did not return a connection object.");
            }

            connection.ConnectionString = connectionString;
            return connection;
        }

        public static void ValidateDataSource(string invariantName, DbDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);

            var factory = factoryCache.GetOrAdd(invariantName, GetFactory).Factory;
            using var expectedConnection = factory.CreateConnection()
                ?? throw new InvalidOperationException($"Database provider factory: '{invariantName}' did not return a connection object.");
            using var actualConnection = dataSource.CreateConnection();

            var expectedType = expectedConnection.GetType();
            var actualType = actualConnection.GetType();
            if (!expectedType.IsAssignableFrom(actualType))
            {
                throw new ArgumentException(
                    $"The configured data source creates connections of type '{actualType.FullName}', but invariant '{invariantName}' uses '{expectedType.FullName}'.",
                    nameof(dataSource));
            }
        }

        private class CachedFactory
        {
            public CachedFactory(DbProviderFactory factory, string factoryName, string factoryDescription, string factoryAssemblyQualifiedNameKey)
            {
                Factory = factory;
                FactoryName = factoryName;
                FactoryDescription = factoryDescription;
                FactoryAssemblyQualifiedNameKey = factoryAssemblyQualifiedNameKey;
            }

            /// <summary>
            /// 提供特定于供应商的功能的工厂。
            /// </summary>
            /// <remarks>有关 <see href="http://florianreischl.blogspot.fi/2011/08/adonet-connection-pooling-internals-and.html">ConnectionPool</see> 的更多信息，
            /// 以及使用此工厂时的问题，请参阅该链接。在评估 Orleans 的健壮性时，请考虑这些说明！</remarks>
            public readonly DbProviderFactory Factory;

            /// <summary>
            /// 数据库连接器供应商设置的已加载工厂名称。
            /// </summary>
            public readonly string FactoryName;

            /// <summary>
            /// 数据库连接器供应商设置的已加载工厂说明。
            /// </summary>
            public readonly string FactoryDescription;

            /// <summary>
            /// 数据库连接器供应商设置的已加载工厂说明。
            /// </summary>
            public readonly string FactoryAssemblyQualifiedNameKey;
        }
    }
}

#nullable restore
