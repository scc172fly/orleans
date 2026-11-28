using System.Data.Common;

namespace Orleans.Configuration
{
    /// <summary>
    /// ADO.NET 提醒器存储选项。
    /// </summary>
    public class AdoNetReminderTableOptions
    {
        /// <summary>
        /// 获取或设置 ADO.NET 固定名称（invariant name）。
        /// </summary>
        public string Invariant { get; set; } = null!;

        /// <summary>
        /// 获取或设置连接字符串。
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
    }
}
