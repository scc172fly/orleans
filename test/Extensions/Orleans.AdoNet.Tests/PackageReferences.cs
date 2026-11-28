using System.Data.Common;

namespace Tester.AdoNet
{
    internal static class PackageReferences
    {
        public static readonly DbProviderFactory[] Factories =
        {
            Microsoft.Data.SqlClient.SqlClientFactory.Instance,
            MySql.Data.MySqlClient.MySqlClientFactory.Instance,
            //Oracle.ManagedDataAccess.Client.OracleClientFactory.Instance, // 目前没有相关测试
            Npgsql.NpgsqlFactory.Instance,
            Microsoft.Data.Sqlite.SqliteFactory.Instance,
            //MySqlConnector.MySqlConnectorFactory.Instance, // 目前没有相关测试
        };
    }
}
