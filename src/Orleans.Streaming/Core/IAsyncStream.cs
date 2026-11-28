using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 此接口表示一个充当生产者与消费者之间分布式会合点的对象。
    /// 它类似于响应式框架中的 <code>Subject</code>，并实现与
    /// <code>IObserver</code> 和 <code>IObservable</code> 对应的异步接口。
    /// </summary>
    /// <typeparam name="T">在流中传输的对象类型。</typeparam>
    public interface IAsyncStream<T> :
        IAsyncStream,
        IEquatable<IAsyncStream<T>>, IComparable<IAsyncStream<T>>, // 比较接口
        IAsyncObservable<T>, IAsyncBatchObservable<T>, // 可观察对象接口
        IAsyncBatchProducer<T> // 观察者接口
    {
        /// <summary>
        /// 获取调用方为此流创建的所有活动订阅的列表。
        /// </summary>
        /// <returns></returns>
        Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles();
    }

    /// <summary>
    /// 此接口表示一个充当生产者与消费者之间分布式会合点的对象。
    /// 它类似于响应式框架中的 <code>Subject</code>，并实现与
    /// <code>IObserver</code> 和 <code>IObservable</code> 对应的异步接口。
    /// </summary>
    public interface IAsyncStream
    {
        /// <summary>
        /// 获取一个值，指示此流是否支持回溯，即是否支持从过去的时间点开始订阅。
        /// </summary>
        /// <returns>如果此流支持回溯，则为 true；否则为 false。</returns>
        bool IsRewindable { get; }

        /// <summary>
        /// 获取提供程序的名称。
        /// </summary>
        /// <value>提供程序的名称。</value>
        string ProviderName { get; }

        /// <summary>
        /// 获取流标识符。
        /// </summary>
        /// <value>流标识符。</value>
        StreamId StreamId { get; }
    }
}
