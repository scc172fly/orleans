using System.Data.Common;

namespace Orleans.GrainDirectory.AdoNet;

/// <summary>
/// ADO.NET Grain 目录的选项。
/// </summary>
public class AdoNetGrainDirectoryOptions
{
    /// <summary>
    /// 获取或设置 ADO.NET 变量。
    /// </summary>
    [Required]
    public string Invariant { get; set; } = "";

    /// <summary>
    /// 获取或设置连接字符串。
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
}
