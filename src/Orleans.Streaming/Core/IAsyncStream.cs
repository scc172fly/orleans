using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 该界面代表一个作为生产者和消费者之间分配的会合对象。
    /// 它类似于 Reactive Framework 的 <code>Subject</code>，并实现
    /// <code>IObserver</code> 或 <code>IObservable</code> 接口。
    /// </summary>
    /// <typeparam name="T">流经流体的物体类型。</typeparam>
    public interface IAsyncStream<T> :
        IAsyncStream,
        IEquatable<IAsyncStream<T>>, IComparable<IAsyncStream<T>>, // 比较对比
        IAsyncObservable<T>, IAsyncBatchObservable<T>, // 可见
        IAsyncBatchProducer<T> // 观察员观察员
    {
        /// <summary>
        /// 获取此流调用者创建的所有活动订阅的列表 。
        /// </summary>
        /// <returns></returns>
        Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles();
    }

    /// <summary>
    /// 该界面代表一个作为生产者和消费者之间分配的会合对象。
    /// 它类似于 Reactive Framework 的 <code>Subject</code>，并实现
    /// <code>IObserver</code> 或 <code>IObservable</code> 接口。
    /// </summary>
    public interface IAsyncStream
    {
        /// <summary>
        /// 获得一个值, 显示是否是一个可回放流 - 支持从上一点点开始签名 。
        /// </summary>
        /// <returns>如果这是可倒转流, 则正确。</returns>
        bool IsRewindable { get; }

        /// <summary>
        /// 获取提供者的名字。
        /// </summary>
        /// <value>提供者的名称。</value>
        string ProviderName { get; }

        /// <summary>
        /// 获取溪流识别码。
        /// </summary>
        /// <value>流体识别符号。</value>
        StreamId StreamId { get; }
    }
}
