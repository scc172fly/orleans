using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans
{
    /// <summary>
    /// 从 <see cref="Grain"/> 或 <see cref="IGrainBase"/> 实现访问流提供程序的扩展方法。
    /// </summary>
    public static class GrainStreamingExtensions
    {
        /// <summary>
        /// 获取具有指定 <paramref name="name"/> 的流提供程序。
        /// </summary>
        /// <param name="grain">Grain。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>流提供程序。</returns>
        public static IStreamProvider GetStreamProvider(this Grain grain, string name)
            => GetStreamProvider((IGrainBase)grain, name);

        /// <summary>
        /// 获取具有指定 <paramref name="name"/> 的流提供程序。
        /// </summary>
        /// <param name="grain">Grain。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>流提供程序。</returns>
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
                // 以前会抛出 KeyNotFoundException，为保持向后兼容性，继续采用这一行为。
                throw new KeyNotFoundException($"Stream provider '{name}' not found", ex);
            }
        }
    }

    /// <summary>
    /// 从客户端访问流提供程序的扩展方法。
    /// </summary>
    public static class ClientStreamingExtensions
    {
        /// <summary>
        /// 获取具有指定 <paramref name="name"/> 的流提供程序。
        /// </summary>
        /// <param name="client">客户端。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>流提供程序。</returns>
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
                // 以前会抛出 KeyNotFoundException，为保持向后兼容性，继续采用这一行为。
                throw new KeyNotFoundException($"Stream provider '{name}' not found", ex);
            }
        }
    }
}
