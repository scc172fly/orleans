using Orleans.Runtime;
using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 提供向消费者和生产者提供流的功能。
    /// </summary>
    public interface IStreamProvider
    {
        /// <summary>
        /// 获取流提供程序的名称。
        /// </summary>
        /// <value>名称。</value>
        string Name { get; }

        /// <summary>
        /// 获取具有指定标识的流。
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamId">流标识符。</param>
        /// <returns>流。</returns>
        IAsyncStream<T> GetStream<T>(StreamId streamId);
        /// <summary>
        /// 获取一个值，指示此提供程序是否支持回溯，即是否支持创建可回溯的流
        /// （允许从过去的时间点开始订阅的流）。
        /// </summary>
        /// <returns>如果此提供程序支持回溯，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        bool IsRewindable { get; }
    }

    /// <summary>
    /// <see cref="IStreamProvider"/> 的扩展方法。
    /// </summary>
    public static class StreamProviderExtensions
    {
        /// <summary>
        /// 获取具有指定标识和命名空间的流。（译注：此重载使用 null 命名空间。）
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="id">标识符。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, Guid id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(null, id));
        }

        /// <summary>
        /// 获取具有指定标识和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="ns">命名空间。</param>
        /// <param name="id">标识符。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string ns, Guid id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(ns, id));
        }

        /// <summary>
        /// 获取具有指定标识和命名空间的流。（译注：此重载使用 null 命名空间。）
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="id">标识符。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(null, id));
        }

        /// <summary>
        /// 获取具有指定标识和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="ns">命名空间。</param>
        /// <param name="id">标识符。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string ns, string id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(ns, id));
        }

        /// <summary>
        /// 获取具有指定标识和命名空间的流。（译注：此重载使用 null 命名空间。）
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="id">标识符。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, long id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(null, id));
        }

        /// <summary>
        /// 获取具有指定标识和命名空间的流。
        /// </summary>
        /// <typeparam name="T">流元素类型。</typeparam>
        /// <param name="streamProvider">流提供程序。</param>
        /// <param name="ns">命名空间。</param>
        /// <param name="id">标识符。</param>
        /// <returns>流。</returns>
        public static IAsyncStream<T> GetStream<T>(this IStreamProvider streamProvider, string ns, long id)
        {
            ArgumentNullException.ThrowIfNull(streamProvider);
            return streamProvider.GetStream<T>(StreamId.Create(ns, id));
        }
    }
}
