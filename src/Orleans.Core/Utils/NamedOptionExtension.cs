using Microsoft.Extensions.Options;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans
{
    /// <summary>
    /// 用于处理命名选项类的扩展方法。
    /// </summary>
    public static class NamedOptionExtensions
    {
        /// <summary>
        /// 获取命名选项实例。
        /// </summary>
        /// <typeparam name="TOption">选项的类型。</typeparam>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">名称。</param>
        /// <returns>TOption。</returns>
        public static TOption GetOptionsByName<TOption>(this IServiceProvider services, string name)
            where TOption : class, new()
        {
            return services.GetRequiredService<IOptionsMonitor<TOption>>().Get(name);
        }
    }
}
