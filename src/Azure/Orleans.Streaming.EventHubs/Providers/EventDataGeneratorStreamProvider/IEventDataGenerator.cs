using Orleans.Runtime;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Streaming.EventHubs.Testing
{
    /// <summary>
    /// 用于测试目的的数据生成器
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IDataGenerator<T>
    {
        /// <summary>
        /// 数据生成器模拟事件读取
        /// </summary>
        /// <param name="maxCount"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        bool TryReadEvents(int maxCount, [NotNullWhen(true)] out IEnumerable<T>? events);
    }

    /// <summary>
    /// StreamDataGeneratingController 控制流数据生成启动和停止
    /// </summary>
    public interface IStreamDataGeneratingController
    {
        /// <summary>
        /// 溪流配置数据生成器
        /// </summary>
        /// <param name="streamId"></param>
        void AddDataGeneratorForStream(StreamId streamId);
        /// <summary>
        /// 要求一条溪停止生产
        /// </summary>
        /// <param name="streamId"></param>
        void StopProducingOnStream(StreamId streamId);
    }

    /// <summary>
    /// 特定流的数据生成器
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IStreamDataGenerator<T> : IDataGenerator<T>
    {
        /// <summary>
        /// 序列号对数
        /// </summary>
        IIntCounter SequenceNumberCounter { set; }
        /// <summary>
        /// 此数据生成器的流流身份
        /// </summary>
        StreamId StreamId { get; }
        /// <summary>
        ///
        /// </summary>
        bool ShouldProduce { set; }
    }

    /// <summary>
    /// 整数对数
    /// </summary>
    public interface IIntCounter
    {
        /// <summary>
        /// 负值
        /// </summary>
        int Value { get; }
        /// <summary>
        /// 计数加起来
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
