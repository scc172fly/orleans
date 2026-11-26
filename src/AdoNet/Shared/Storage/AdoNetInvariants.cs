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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 拥有一个已知的,销售商特定连接器类变式名名的持有人。
    /// </summary>
    internal static class AdoNetInvariants
    {
        /// <summary>
        /// 支持的变量列表 。
        /// </summary>
        /// <remarks>这里的变量名称与命名空间不符, 通常公约也如此。
        /// 目前的例外是 MySQL 使用与 MySQL 比较对比
        /// 至正式甲骨文分布。</remarks>
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
        /// Microsoft SQL 服务器变量名称字符串。
        /// </summary>
        public const string InvariantNameSqlServer = "Microsoft.Data.SqlClient";

        /// <summary>
        /// Oracle 数据库服务器变量名称字符串。
        /// </summary>
        public const string InvariantNameOracleDatabase = "Oracle.DataAccess.Client";

        /// <summary>
        /// SQLite 变量名称字符串。
        /// </summary>
        public const string InvariantNameSqlLite = "System.Data.SQLite";

        /// <summary>
        /// MySql 变量名称字符串。
        /// </summary>
        public const string InvariantNameMySql = "MySql.Data.MySqlClient";

        /// <summary>
        /// PostgreSQL 变量名称字符串。
        /// </summary>
        public const string InvariantNamePostgreSql = "Npgsql";

        /// <summary>
        /// Dotnet 核心 Microsoft SQL 服务器变量名称字符串。
        /// </summary>
        [Obsolete("Use InvariantNameSqlServer instead.")]
        public const string InvariantNameSqlServerDotnetCore = InvariantNameSqlServer;

        /// <summary>
        /// 开源连接库的启用 MySQL 连接器库。
        /// </summary>
        public const string InvariantNameMySqlConnector = "MySql.Data.MySqlConnector";
    }
}
