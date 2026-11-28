using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
    /// <summary>
    /// 保存已知的各供应商专用连接器类的固定名称（invariant name）。
    /// </summary>
    internal static class AdoNetInvariants
    {
        /// <summary>
        /// 受支持的固定名称列表。
        /// </summary>
        /// <remarks>这里的固定名称不像通常的约定那样与命名空间一致。
        /// 目前的例外是 MySQL Connector 库，它使用与 Oracle 官方发行版中的 MySQL 相同的
        /// 固定名称。</remarks>
        public static ICollection<string> Invariants { get; } = new Collection<string>(new List<string>(new[]
        {
            InvariantNameMySql,
            InvariantNameOracleDatabase,
            InvariantNamePostgreSql,
            InvariantNameSqlLite,
            InvariantNameSqlServer,
            InvariantNameMySqlConnector
        }));

        /// <summary>
        /// Microsoft SQL Server 固定名称字符串。
        /// </summary>
        public const string InvariantNameSqlServer = "Microsoft.Data.SqlClient";

        /// <summary>
        /// Oracle Database 服务器固定名称字符串。
        /// </summary>
        public const string InvariantNameOracleDatabase = "Oracle.DataAccess.Client";

        /// <summary>
        /// SQLite 固定名称字符串。
        /// </summary>
        public const string InvariantNameSqlLite = "System.Data.SQLite";

        /// <summary>
        /// MySQL 固定名称字符串。
        /// </summary>
        public const string InvariantNameMySql = "MySql.Data.MySqlClient";

        /// <summary>
        /// PostgreSQL 固定名称字符串。
        /// </summary>
        public const string InvariantNamePostgreSql = "Npgsql";

        /// <summary>
        /// .NET Core Microsoft SQL Server 固定名称字符串。
        /// </summary>
        [Obsolete("Use InvariantNameSqlServer instead.")]
        public const string InvariantNameSqlServerDotnetCore = InvariantNameSqlServer;

        /// <summary>
        /// MySQL 连接器库的开源实现。
        /// </summary>
        public const string InvariantNameMySqlConnector = "MySql.Data.MySqlConnector";
    }
}
