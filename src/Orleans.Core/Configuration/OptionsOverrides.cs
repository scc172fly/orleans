
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration.Overrides
{
    /// <summary>
    /// 使用命名选项覆盖配置的功能，未指定时回退到默认（未命名）值。
    /// </summary>
    public static class OptionsOverrides
    {
        /// <summary>
        /// 获取可能已针对各个提供程序进行覆盖的 <see cref="ClusterOptions"/>。
        /// 注意：此功能用于迁移，以处理此前各提供程序在使用 ServiceId 和 ClusterId 时存在的不一致行为。
        /// </summary>
        public static IOptions<ClusterOptions> GetProviderClusterOptions(this IServiceProvider services, string providerName) => services.GetOverridableOption<ClusterOptions>(providerName);

        /// <summary>
        /// 获取可由命名服务覆盖的选项。
        /// </summary>
        private static IOptions<TOptions> GetOverridableOption<TOptions>(this IServiceProvider services, string key)
            where TOptions : class, new()
        {
            TOptions? option = services.GetKeyedService<TOptions>(key);
            return option != null
                ? Options.Create(option)
                : services.GetRequiredService<IOptions<TOptions>>();
        }
    }
}
