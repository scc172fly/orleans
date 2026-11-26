using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans
{
    /// <summary>
    /// 从 <see cref="Grain"/> 或 <see cref="IGrainBase"/> 执行中访问流源提供者的扩展方法。
    /// </summary>
    public static class GrainStreamingExtensions
    {
        /// <summary>
        /// 获取指定的 <paramref name="name"/>的流源提供商。
        /// </summary>
        /// <param name="grain">下 grain.</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>流源提供者。</returns>
        public static IStreamProvider GetStreamProvider(this Grain grain, string name)
            => GetStreamProvider((IGrainBase)grain, name);

        /// <summary>
        /// 获取指定的 <paramref name="name"/>的流源提供商。
        /// </summary>
        /// <param name="grain">下 grain.</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>流源提供者。</returns>
        public static IStreamProvider GetStreamProvider(this IGrainBase grain, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            try
            {
                return grain.GrainContext.ActivationServices.GetRequiredKeyedService<IStreamProvider>(name);
            }
            catch (InvalidOperationException ex)
            {
                // 我们以前是抛 KeyNotFoundException 之前,保持这样,后向兼容性
                throw new KeyNotFoundException($"Stream provider '{name}' not found", ex);
            }
        }
    }

    /// <summary>
    /// 用户访问流源供应商的扩展方法。
    /// </summary>
    public static class ClientStreamingExtensions
    {
        /// <summary>
        /// 获取指定的 <paramref name="name"/>的流源提供商。
        /// </summary>
        /// <param name="client">客户。</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>流源提供者。</returns>
        public static IStreamProvider GetStreamProvider(this IClusterClient client, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            try
            {
                return client.ServiceProvider.GetRequiredKeyedService<IStreamProvider>(name);
            }
            catch (InvalidOperationException ex)
            {
                // 我们以前是抛 KeyNotFoundException 之前,保持这样,后向兼容性
                throw new KeyNotFoundException($"Stream provider '{name}' not found", ex);
            }
        }
    }
}
