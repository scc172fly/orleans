using System.Data.Common;

namespace Orleans.Configuration;

/// <summary>
/// ADO.NET 流选项。
/// </summary>
public class AdoNetStreamOptions
{
    /// <summary>
    /// 获取或设置 ADO.NET 固定名称（invariant name）。
    /// </summary>
    public string Invariant { get; set; } = default!;

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

    /// <summary>
    /// 消息的最大投递尝试次数。
    /// 达到此次数仍未成功时，消息最终会移入死信。
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// 消息尚未确认时，允许再次出队前的超时时长。
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 消息被视为过期并移入死信的超时时长，与尝试次数无关。
    /// 只有当前尝试也已超过可见性超时时，才会移动该消息。
    /// </summary>
    public TimeSpan ExpiryTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 失败消息从死信表中删除前的超时时长。
    /// </summary>
    public TimeSpan DeadLetterEvictionTimeout { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 两次清理活动之间的时间间隔。
    /// 这些活动包括将过期消息移入死信，以及在死信自身的生存期结束后移除死信。
    /// 此周期在整个集群范围内生效，不会随 Silo 数量而改变。
    /// </summary>
    public TimeSpan EvictionInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 每批清理操作所影响的最大消息数量。
    /// </summary>
    public int EvictionBatchSize { get; set; } = 1000;

    /// <summary>
    /// 底层数据库初始化的安全超时时长。
    /// </summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
