using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace Orleans.TestingHost;

/// <summary>
/// 用于覆盖各区域通过键控依赖注入解析得到的 <see cref="TimeProvider"/> 实例的
/// 测试辅助工具。参见 <see cref="TimeProviderNames"/>。
/// </summary>
public static class TimeProviderTestingExtensions
{
    /// <summary>
    /// 覆盖各键控区域的 <see cref="TimeProvider"/>，但面向 Grain 的区域
    /// （<see cref="TimeProviderNames.Grains"/>）除外，它仍解析默认的（无键）提供程序。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 适用于安装可控时钟作为默认 <see cref="TimeProvider"/> 的测试，以便
    /// 确定性地驱动 Grain 定时器，同时使 Silo 的后台维护定时器仍使用真实
    /// 时间，从而在推进时钟时不会内联恢复这些循环。
    /// </para>
    /// <para>
    /// 不枚举每个区域键（否则会使此辅助工具耦合到扩展包拥有的键），
    /// 而是注册一个通配的 <see cref="KeyedService.AnyKey"/> 提供程序，使每个键控区域都解析提供的
    /// <paramref name="timeProvider"/>，然后重新将 <see cref="TimeProviderNames.Grains"/> 固定到默认提供程序。
    /// 精确键匹配优先于通配注册。
    /// </para>
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="timeProvider">后台区域使用的时间提供程序。</param>
    public static IServiceCollection UseTimeProviderForBackgroundAreas(this IServiceCollection services, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // 将每个键控区域固定到提供的提供程序（通常为真实时间）……
        services.AddKeyedSingleton(KeyedService.AnyKey, (sp, _) => timeProvider);

        // ……但面向 Grain 的区域除外，它仍解析默认的（无键）提供程序，使 Grain 定时器
        // 和 Grain 端的延迟继续使用测试时钟。
        services.AddKeyedSingleton(TimeProviderNames.Grains, static (sp, _) => sp.GetRequiredService<TimeProvider>());

        return services;
    }
}
