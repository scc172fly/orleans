using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Serialization.Serializers;

namespace Orleans.Storage
{

    /// <summary>
    /// <see cref="MemoryGrainStorageWithLatency"/> 存储提供者的选项。
    /// </summary>
    public class MemoryStorageWithLatencyOptions : MemoryGrainStorageOptions
    {
        /// <summary>
        /// 默认的延迟时间 。
        /// </summary>
        public static readonly TimeSpan DefaultLatency = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// 固定或固定延绳
        /// </summary>
        /// <value>潜伏期</value>
        public TimeSpan Latency { get; set; } = DefaultLatency;

        /// <summary>
        /// 获取或设定一个数值,表明是否要模拟电话而不是发布真正的存储电话。
        /// </summary>
        /// <value><see langword="true" /> 如果提供者应模拟电话,否则, <see langword="false" />。</value>
        public bool MockCallsOnly { get; set; }
    }

    /// <summary>
    /// 这是对储存供应商的简单即时实施,提供固定的存储电话。
    /// 这一类可用于系统测试和研究储存潜伏的影响。
    /// </summary>
    /// <remarks>
    /// 这个存储提供者只用于简单的模拟试验情景。
    /// 这一类不应用于生产环境, 
    ///  由于[按设计 无法提供任何抗御力 
    ///  或长期持久性能力。
    /// </remarks>
    [DebuggerDisplay("MemoryStore:{Name},WithLatency:{latency}")]
    public class MemoryGrainStorageWithLatency : IGrainStorage, IDisposable
    {
        private readonly MemoryGrainStorage baseGranStorage;
        private readonly MemoryStorageWithLatencyOptions options;
        /// <summary> 默认构建器。 </summary>
        public MemoryGrainStorageWithLatency(
            string name,
            MemoryStorageWithLatencyOptions options,
            ILoggerFactory loggerFactory,
            IGrainFactory grainFactory,
            IActivatorProvider activatorProvider,
            IGrainStorageSerializer defaultGrainStorageSerializer)
        {
            this.baseGranStorage = new MemoryGrainStorage(
                name,
                options,
                loggerFactory.CreateLogger<MemoryGrainStorage>(),
                grainFactory,
                defaultGrainStorageSerializer,
                activatorProvider);
            this.options = options;
        }

        /// <summary> 为此存储提供者读取国家数据函数。 </summary>
        /// <see cref="IGrainStorage.ReadStateAsync{T}(string, GrainId, IGrainState{T})"/>
        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            return MakeFixedLatencyCall(() => baseGranStorage.ReadStateAsync(grainType, grainId, grainState));
        }

        /// <summary> 为此存储提供者写入状态数据函数 。 </summary>
        /// <see cref="IGrainStorage.WriteStateAsync{T}(string, GrainId, IGrainState{T})"/>
        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            return MakeFixedLatencyCall(() => baseGranStorage.WriteStateAsync(grainType, grainId, grainState));
        }

        /// <summary> 删除此存储提供者的 / 清除状态数据函数。 </summary>
        /// <see cref="IGrainStorage.ClearStateAsync{T}(string, GrainId, IGrainState{T})"/>
        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            return MakeFixedLatencyCall(() => baseGranStorage.ClearStateAsync(grainType, grainId, grainState));
        }

        /// <summary>
        /// 释放出该储存提供者拥有的资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放出该储存提供者拥有的资源。
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true" /> 释放管理的资源;否则, <see langword="false" />。
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                baseGranStorage.Dispose();
            }
        }

        private async Task MakeFixedLatencyCall(Func<Task> action)
        {
            var sw = Stopwatch.StartNew();
            Exception? error = null;
            try
            {
                if (this.options.MockCallsOnly)
                {
                    // 稍有延迟,模拟通话
                    await Task.Delay(10);
                }
                else
                {
                    // 做真正的决定
                    await action();
                }
            }
            catch (Exception exc)
            {
                error = exc;
            }

            do
            {
                // 确定余下的等待时间,使这一操作超过所需的延迟时间。
                // 并增加一个额外减速系数,以计算任何系统时钟解决方案边缘案例。
                var extraDelay = TimeSpan.FromTicks(
                     5 * TimeSpan.TicksPerMillisecond + this.options.Latency.Ticks - sw.Elapsed.Ticks);

                if (extraDelay > TimeSpan.Zero)
                {
                    await Task.Delay(extraDelay);
                }
                else
                {
                    break;
                }
            } while (true);

            Debug.Assert(sw.Elapsed >= this.options.Latency, "sw.Elapsed >= this.options.Latency");
            if (error != null)
            {
                // 以 AggregateException 换行,以便保存原始错误堆栈跟踪。
                throw new AggregateException(error);
            }
        }
    }
}
