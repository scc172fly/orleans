
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration.Overrides
{
    /// <summary>
    /// 使用指定选项,回归到默认(未命名)值后,可支配性在多个选项上居于首位。
    /// </summary>
    public static class OptionsOverrides
    {
        /// <summary>
        /// 获得每个提供者可能已经推翻的 <see cref="ClusterOptions"/> 。
        /// 注:此表用于移徙目的,作为处理供应商使用 ServiceId 和 ClusterId的方式以往不一致行为的一种手段。
        /// </summary>
        public static IOptions<ClusterOptions> GetProviderClusterOptions(this IServiceProvider services, string providerName) => services.GetOverridableOption<ClusterOptions>(providerName);

        /// <summary>
        /// 获得可被指定服务推翻的选项 。
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
