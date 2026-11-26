using System.Data.Common;

namespace Orleans.Configuration;

/// <summary>
/// ADO.NET 流的选项。
/// </summary>
public class AdoNetStreamOptions
{
    /// <summary>
    /// 获取或设置 ADO.NET 变量。
    /// </summary>
    public string Invariant { get; set; } = default!;

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

    /// <summary>
    /// 传递信息尝试的最大次数。
    /// 如果这些多次尝试没有成功,最终信息就会变成一纸空文。
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// 超时, 如果尚未确认的话, 允许再次解密信件 。
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 过期超时, 直到信件被视为过期并移动到已绝信, 不管尝试过与否 。
    /// 只有在当前尝试也超过其可见度超时的情况下,才移动信息。
    /// </summary>
    public TimeSpan ExpiryTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 删除超时, 直到从废信表中删除失败的信件 。
    /// </summary>
    public TimeSpan DeadLetterEvictionTimeout { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 迁离活动之间的时间间隔。
    /// 其中包括将过期信息移至死信,在他们自己的一生中删除死亡信件。
    /// 这一时期的组群范围很广,不会随着 silos的数量而变化。
    /// </summary>
    public TimeSpan EvictionInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 受驱逐批次影响的信息的最大数量。
    /// </summary>
    public int EvictionBatchSize { get; set; } = 1000;

    /// <summary>
    /// 数据库初始化的安全超时。
    /// </summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
