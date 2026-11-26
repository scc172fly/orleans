namespace Orleans.Runtime;

/// <summary>
/// 用于解决每个地区从按需打针的 <see cref="System.TimeProvider"/> 个案的服务键
/// 区域所有区域 Orleans 单个扩展包(重新定位、流流、交易、
/// 持续工作、日记或类似的工作)在相应的软件包中定义自己的密钥。
/// </summary>
/// <remarks>
/// <para>
/// Orleans 解析单个默认 <see cref="System.TimeProvider"/> 登记的时间。允许个人
/// 由不同时钟驱动的子系统,每个区域还解析一个 <em>键</em> <see cref="System.TimeProvider"/>
/// 使用其中的密钥之一。 A 全部套接 <see cref="Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey"/>
/// 注册解决了无密钥默认提供方的每一个密钥,所以行为不变,除非有特定区域
/// 被压倒。
/// </para>
/// <para>
/// 这主要可用于测试:测试可以安装可控时钟(例如
/// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c>作为默认驱动器驱动 grain 计时器和
/// grain- 以确定性延迟为定点,同时将 silo的背景维护计时器与实时定位相连接,以便
/// 推进假时钟不会恢复这些循环的内嵌。
/// </para>
/// </remarks>
public static class TimeProviderNames
{
    /// <summary>
    /// 时钟暴露在 grains 经由 <c>IGrainRuntime.TimeProvider</c> 驱动时 grain 计时器。
    /// </summary>
    public const string Grains = "Orleans.Grains";

    /// <summary>
    /// 信息通信基础设施所用的时钟,如请求超时跟踪和维护网关。
    /// </summary>
    public const string Messaging = "Orleans.Messaging";

    /// <summary>
    /// silo 背景维护环所使用的时钟由 <c>IAsyncTimerFactory</c>驱动
    /// (grain 目录维护及类似),不包括使用
    /// <see cref="Membership"/>.
    /// </summary>
    public const string SystemTimers = "Orleans.SystemTimers";

    /// <summary>
    /// 包括成员代理、成员表在内的分组成员循环利用的时钟
    /// 和清除, silo 健康监测和检测。
    /// </summary>
    public const string Membership = "Orleans.Membership";

    /// <summary>
    /// 激活生命周期管理所用的时钟,包括启动收集、偏差检查、
    /// 重新分割,再平衡
    /// </summary>
    public const string ActivationManagement = "Orleans.ActivationManagement";

    /// <summary>
    /// grain 目录缓存过期时所用的时钟。
    /// </summary>
    public const string GrainDirectory = "Orleans.GrainDirectory";
}
