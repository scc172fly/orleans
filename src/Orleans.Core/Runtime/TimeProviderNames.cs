namespace Orleans.Runtime;

/// <summary>
/// 用于通过依赖注入解析各个领域的 <see cref="System.TimeProvider"/> 实例的服务键，适用于
/// Orleans 核心运行时负责的领域。各个扩展包（提醒器、流处理、事务、
/// 持久化作业、日志持久化等）在相应包中定义各自的键。
/// </summary>
/// <remarks>
/// <para>
/// Orleans 通过单个默认的 <see cref="System.TimeProvider"/> 注册获取时间。为了允许各个
/// 子系统由不同的时钟驱动，每个领域也会解析一个 <em>带键的</em> <see cref="System.TimeProvider"/>，
/// 使用这些键之一。一个兜底的 <see cref="Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey"/>
/// 注册会将每个键解析为不带键的默认提供程序，因此除非对特定领域
/// 进行覆盖，否则行为保持不变。
/// </para>
/// <para>
/// 这主要用于测试：测试可以安装可控时钟（例如
/// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c>）作为默认提供程序，以确定性方式驱动 Grain 定时器和
/// 面向 Grain 的延迟，同时将 Silo 后台维护定时器固定为使用真实时间，从而使
/// 推进虚拟时钟不会内联恢复这些后台循环。
/// </para>
/// </remarks>
public static class TimeProviderNames
{
    /// <summary>
    /// 通过 <c>IGrainRuntime.TimeProvider</c> 向 Grain 公开、并用于驱动 Grain 定时器的时钟。
    /// </summary>
    public const string Grains = "Orleans.Grains";

    /// <summary>
    /// 消息传递基础设施使用的时钟，例如用于请求超时跟踪及网关维护。
    /// </summary>
    public const string Messaging = "Orleans.Messaging";

    /// <summary>
    /// 由 <c>IAsyncTimerFactory</c> 驱动的 Silo 后台维护循环使用的时钟
    /// （例如 Grain 目录维护），但不包括集群成员资格管理，后者使用
    /// <see cref="Membership"/>.
    /// </summary>
    public const string SystemTimers = "Orleans.SystemTimers";

    /// <summary>
    /// 集群成员资格管理循环使用的时钟，包括成员资格代理、成员表
    /// 维护与清理，以及 Silo 健康监控和探测。
    /// </summary>
    public const string Membership = "Orleans.Membership";

    /// <summary>
    /// 激活实例生命周期管理使用的时钟，包括激活实例回收、可迁移性检查、
    /// 重新分区及再均衡。
    /// </summary>
    public const string ActivationManagement = "Orleans.ActivationManagement";

    /// <summary>
    /// Grain 目录缓存用于条目过期判断的时钟。
    /// </summary>
    public const string GrainDirectory = "Orleans.GrainDirectory";
}
