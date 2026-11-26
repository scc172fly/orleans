using System.Data.Common;

namespace Orleans.Configuration
{
    /// <summary>
    /// ADO.NET 组合群选项
    /// </summary>
    public class AdoNetClusteringSiloOptions
    {
        /// <summary>
        /// AdoNet 存储的连接字符串
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
        /// 成员数据库连接器的变量名称 。
        /// </summary>
        public string Invariant { get; set; } = null!;
    }
}
