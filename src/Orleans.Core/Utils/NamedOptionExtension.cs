using Microsoft.Extensions.Options;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans
{
    /// <summary>
    /// 命名选项类工作的扩展。
    /// </summary>
    public static class NamedOptionExtensions
    {
        /// <summary>
        /// 获得一个命名选项实例。
        /// </summary>
        /// <typeparam name="TOption">t 选项的类型。</typeparam>
        /// <param name="services">服务。</param>
        /// <param name="name">名称。</param>
        /// <returns>备选。</returns>
        public static TOption GetOptionsByName<TOption>(this IServiceProvider services, string name)
            where TOption : class, new()
        {
            return services.GetRequiredService<IOptionsMonitor<TOption>>().Get(name);
        }
    }
}
