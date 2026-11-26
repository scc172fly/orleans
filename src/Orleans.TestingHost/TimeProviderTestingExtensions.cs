using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace Orleans.TestingHost;

/// <summary>
/// 超过按键依赖性解决的每个区域 <see cref="TimeProvider"/> 例数的测试辅助器
/// 。见 <see cref="TimeProviderNames"/>。
/// </summary>
public static class TimeProviderTestingExtensions
{
    /// <summary>
    /// 覆盖 <see cref="TimeProvider"/> 每一加以键的区域,除有 grain- 远方区域
    /// (<see cref="TimeProviderNames.Grains"/>),它继续解决违约(蒙眼)提供者的问题。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是用于安装可控时钟作为默认 <see cref="TimeProvider"/> 的测试。
    /// 驱动 grain 定时器,但需要 silo的背景维护计时器才能保持真实
    /// 时间,以便推进时钟不会恢复这些循环。
    /// </para>
    /// <para>
    /// 而不是列出每个区域密钥( 它将将此辅助器与扩展包拥有的密钥相配) ,
    /// 键盘中注册一个全套 <see cref="KeyedService.AnyKey"/> 提供者,这样每个密钥区域都解决了所提供的
    /// <paramref name="timeProvider"/>,然后将 <see cref="TimeProviderNames.Grains"/> 重新输入到默认提供者。
    /// 准确的关键匹配优先于全套登记。
    /// </para>
    /// </remarks>
    /// <param name="services">服务收藏。</param>
    /// <param name="timeProvider">背景区域所用时间提供者。</param>
    public static IServiceCollection UseTimeProviderForBackgroundAreas(this IServiceCollection services, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // 将每个密钥区都插入提供供应商(通常实时)...
        services.AddKeyedSingleton(KeyedService.AnyKey, (sp, _) => timeProvider);

        // ...但 grain- 向上区域除外,该区域一直解决默认(光眼)提供者,以便 grain 计时器
        // 和 grain-侧延候继续观察测试钟。
        services.AddKeyedSingleton(TimeProviderNames.Grains, static (sp, _) => sp.GetRequiredService<TimeProvider>());

        return services;
    }
}
