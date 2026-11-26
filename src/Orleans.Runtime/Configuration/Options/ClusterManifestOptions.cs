namespace Orleans.Configuration;

/// <summary>
/// 配置 silo 从集中检索 grain 列表的方式。
/// </summary>
public sealed class ClusterManifestOptions
{
    /// <summary>
    /// 获取或设置值,以显示 silo 是否通过内容散列检索列表
    /// 并使用同行摘要来弥补缺失的旅客名单。
    /// </summary>
    /// <value>
    /// 默认<see langword="true"/> 。 设为 <see langword="false"/> 直接检索每个活动 silo的列表 。
    /// </value>
    /// <remarks>
    /// 当 silo的列表提供者构建时,运行时间会捕捉到此设置 。
    /// 重新启动 silo 以应用更改的值。 Silos 服务于来自启用端点的散列请求
    /// 需要时, 包括当他们自己配置的检索可使用直接路径时 。
    /// 同侪修理最多利用三个同时进行的地方尝试,每次尝试的最后期限为1秒钟。
    /// </remarks>
    public bool EnableContentAddressedRetrieval { get; set; } = true;
}
