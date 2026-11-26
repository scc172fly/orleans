using System.Data.Common;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置客户端网关发现时的关联数据库访问权限。
    /// </summary>
    public class AdoNetClusteringClientOptions
    {
        /// <summary>
        /// Sql 的连接字符串
        /// </summary>
        [Redact]
        public string? ConnectionString { get; set; }

        /// <summary>
        /// 获取或设置打开数据库连接所使用的数据源。
        /// </summary>
        /// <remarks>
        /// 数据源由调用者拥有, Orleans不处置。
        /// </remarks>
        [Redact]
        public DbDataSource? DataSource { get; set; }

        /// <summary>
        /// gatewayProvider数据库连接器的变量名称。
        /// </summary>
        public string Invariant { get; set; } = null!;
    }
}
