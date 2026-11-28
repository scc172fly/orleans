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
    /// <see cref="MemoryGrainStorageWithLatency"/> 存储提供程序的选项。
    /// </summary>
    public class MemoryStorageWithLatencyOptions : MemoryGrainStorageOptions
    {
        /// <summary>
        /// 默认延迟。
        /// </summary>
        public static readonly TimeSpan DefaultLatency = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// 获取或设置延迟。
        /// </summary>
        /// <value>延迟。</value>
        public TimeSpan Latency { get; set; } = DefaultLatency;

        /// <summary>
        /// 获取或设置指示是否模拟调用而非发起真实存储调用的值。
        /// </summary>
        /// <value><see langword="true" />，如果提供程序应模拟调用；否则为 <see langword="false" />。</value>
        public bool MockCallsOnly { get; set; }
    }

    /// <summary>
    /// 这是一个简单的内存存储提供程序实现，它使存储调用具有固定延迟。
    /// 此类可用于系统测试以及研究存储延迟的影响。
    /// </summary>
    /// <remarks>
    /// 此存储提供程序仅适用于简单的内存测试场景。
    /// 此类不得用于生产环境，
    /// 因为根据其设计，它不提供任何故障恢复能力
    /// 或长期持久化能力。
    /// </remarks>
    [DebuggerDisplay("MemoryStore:{Name},WithLatency:{latency}")]
    public class MemoryGrainStorageWithLatency : IGrainStorage, IDisposable
    {
        private readonly MemoryGrainStorage baseGranStorage;
        private readonly MemoryStorageWithLatencyOptions options;
        /// <summary>默认构造函数。</summary>
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

        /// <summary>此存储提供程序的状态数据读取方法。</summary>
        /// <see cref="IGrainStorage.ReadStateAsync{T}(string, GrainId, IGrainState{T})"/>
        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            return MakeFixedLatencyCall(() => baseGranStorage.ReadStateAsync(grainType, grainId, grainState));
        }

        /// <summary>此存储提供程序的状态数据写入方法。</summary>
        /// <see cref="IGrainStorage.WriteStateAsync{T}(string, GrainId, IGrainState{T})"/>
        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            return MakeFixedLatencyCall(() => baseGranStorage.WriteStateAsync(grainType, grainId, grainState));
        }

        /// <summary>此存储提供程序的状态数据删除或清除方法。</summary>
        /// <see cref="IGrainStorage.ClearStateAsync{T}(string, GrainId, IGrainState{T})"/>
        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            return MakeFixedLatencyCall(() => baseGranStorage.ClearStateAsync(grainType, grainId, grainState));
        }

        /// <summary>
        /// 释放此存储提供程序拥有的资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放此存储提供程序拥有的资源。
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true" /> 表示释放托管资源；否则为 <see langword="false" />。
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
                    // 带有少量延迟的模拟调用。
                    await Task.Delay(10);
                }
                else
                {
                    // 发起真实调用。
                    await action();
                }
            }
            catch (Exception exc)
            {
                error = exc;
            }

            do
            {
                // 计算剩余等待时间，使操作耗时超过要求的 Latency。
                // 另加少量裕量，以应对系统时钟精度导致的边界情况。
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
                // 包装在 AggregateException 中，以保留原始错误的堆栈跟踪。
                throw new AggregateException(error);
            }
        }
    }
}
