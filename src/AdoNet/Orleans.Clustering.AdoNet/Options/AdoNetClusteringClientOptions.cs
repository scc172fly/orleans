using System.Data.Common;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置客户端发现网关所需的关系数据库访问。
    /// </summary>
    public class AdoNetClusteringClientOptions
    {
        /// <summary>
        /// SQL 连接字符串。
        /// </summary>
        [Redact]
        public string? ConnectionString { get; set; }

        /// <summary>
        /// 获取或设置用于打开数据库连接的数据源。
        /// </summary>
        /// <remarks>
        /// 数据源由调用方拥有，Orleans 不负责释放它。
        /// </remarks>
        [Redact]
        public DbDataSource? DataSource { get; set; }

        /// <summary>
        /// 网关提供程序数据库连接器的固定名称（invariant name）。
        /// </summary>
        public string Invariant { get; set; } = null!;
    }
}
