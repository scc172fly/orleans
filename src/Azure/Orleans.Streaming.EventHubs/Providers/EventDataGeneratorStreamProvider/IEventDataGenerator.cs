using Orleans.Runtime;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 用于测试的数据生成器。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IDataGenerator<T>
    {
        /// <summary>
        /// 数据生成器模拟事件读取过程。
        /// </summary>
        /// <param name="maxCount"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        bool TryReadEvents(int maxCount, [NotNullWhen(true)] out IEnumerable<T>? events);
    }

    /// <summary>
    /// StreamDataGeneratingController 控制流数据生成的启动和停止。
    /// </summary>
    public interface IStreamDataGeneratingController
    {
        /// <summary>
        /// 为流配置数据生成器。
        /// </summary>
        /// <param name="streamId"></param>
        void AddDataGeneratorForStream(StreamId streamId);
        /// <summary>
        /// 请求一个流停止生成数据。
        /// </summary>
        /// <param name="streamId"></param>
        void StopProducingOnStream(StreamId streamId);
    }

    /// <summary>
    /// 特定流的数据生成器。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IStreamDataGenerator<T> : IDataGenerator<T>
    {
        /// <summary>
        /// 序列号计数器。
        /// </summary>
        IIntCounter SequenceNumberCounter { set; }
        /// <summary>
        /// 此数据生成器对应的流标识。
        /// </summary>
        StreamId StreamId { get; }
        /// <summary>
        ///
        /// </summary>
        bool ShouldProduce { set; }
    }

    /// <summary>
    /// 整数计数器。
    /// </summary>
    public interface IIntCounter
    {
        /// <summary>
        /// 计数器值。
        /// </summary>
        int Value { get; }
        /// <summary>
        /// 递增计数器。
        /// </summary>
        void Increment();
    }

    internal class IntCounter : IIntCounter
    {
        private int counter = 0;
        public int Value { get { return this.counter; } }
        public void Increment()
        {
            counter++;
        }
    }
}
