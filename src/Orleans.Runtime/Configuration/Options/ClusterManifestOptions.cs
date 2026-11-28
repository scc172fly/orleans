namespace Orleans.Configuration;

/// <summary>
/// 配置 Silo 从集群获取 Grain 清单的方式。
/// </summary>
public sealed class ClusterManifestOptions
{
    /// <summary>
    /// 获取或设置一个值，指示 Silo 是否按内容哈希获取清单，
    /// 并使用对等节点的摘要修复缺失的清单。
    /// </summary>
    /// <value>
    /// 默认为 <see langword="true"/>。设为 <see langword="false"/> 时直接获取每个活动 Silo 的清单。
    /// </value>
    /// <remarks>
    /// 运行时在构造 Silo 的清单提供程序时读取此设置。
    /// 更改此值后需要重启 Silo 才能生效。Silo 按需响应已启用此功能的对等节点发出的哈希请求，
    /// 即使自身已配置为采用直接获取路径也会如此。
    /// 对等节点修复最多使用三次并发的本地尝试，每次限时一秒。
    /// </remarks>
    public bool EnableContentAddressedRetrieval { get; set; } = true;
}
