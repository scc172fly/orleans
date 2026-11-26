using Orleans.Runtime;
using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 向消费者和生产者提供流动资金的功能。
    /// </summary>
    public interface IStreamProvider
    {
        /// <summary>
        /// 获取流提供商的名称 。
        /// </summary>
        /// <value>名称。</value>
        string Name { get; }

        /// <summary>
        /// 获取有指定身份的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>流。</returns>
        IAsyncStream<T> GetStream<T>(StreamId streamId);
        /// <summary>
        /// 获得一个值, 显示此是否是一个可回溯的提供者 - 支持创建可回溯流 
        /// (允许从上一个时间点签名的流)。
        /// </summary>
        /// <returns><see langword="true"/> 如果这是可变现的提供者, <see langword="false"/> 否则。</returns>
        bool IsRewindable { get; }
    }

    /// <summary>
    /// <see cref="IStreamProvider"/>的扩展期。
    /// </summary>
    public static class StreamProviderExtensions
    {
        /// <summary>
        /// 获取有指定身份和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="id">识别符号。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, Guid id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(null, id));
        }

        /// <summary>
        /// 获取有指定身份和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="ns">命名空间。</param>
        /// <param name="id">识别符号。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string ns, Guid id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(ns, id));
        }

        /// <summary>
        /// 获取有指定身份和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="id">识别符号。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(null, id));
        }

        /// <summary>
        /// 获取有指定身份和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="ns">命名空间。</param>
        /// <param name="id">识别符号。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string ns, string id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(ns, id));
        }

        /// <summary>
        /// 获取有指定身份和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="id">识别符号。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, long id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(null, id));
        }

        /// <summary>
        /// 获取有指定身份和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流体元素类型。</typeparam>
        /// <param name="streamProvider">流源提供者。</param>
        /// <param name="ns">命名空间。</param>
        /// <param name="id">识别符号。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string ns, long id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(ns, id));
        }
    }
}
