using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using Orleans.Runtime;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// GeneratorStreamProvider 和 MemoryStreamProvider 默认使用的内存块池监视器。
    /// </summary>
    public class DefaultBlockPoolMonitor : IBlockPoolMonitor
    {
        /// <summary>
        /// 附加到此监视器报告的指标上的维度。
        /// </summary>
        protected KeyValuePair<string, object>[] _dimensions;
        private readonly ObservableCounter<long> _totalMemoryCounter;
        private readonly ObservableCounter<long> _availableMemoryCounter;
        private readonly ObservableCounter<long> _claimedMemoryCounter;
        private readonly ObservableCounter<long> _releasedMemoryCounter;
        private readonly ObservableCounter<long> _allocatedMemoryCounter;
        private long _totalMemory;
        private long _availableMemory;
        private long _claimedMemory;
        private long _releasedMemory;
        private long _allocatedMemory;

        /// <summary>
        /// 初始化 <see cref="DefaultBlockPoolMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">内存块池指标维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        public DefaultBlockPoolMonitor(BlockPoolMonitorDimensions dimensions, OrleansInstruments instruments)
            : this(CreateDimensions(dimensions), GetMeter(instruments))
        {
        }

        /// <summary>
        /// 初始化 <see cref="DefaultBlockPoolMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">附加到此监视器报告的指标上的维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        protected DefaultBlockPoolMonitor(KeyValuePair<string, object>[] dimensions, OrleansInstruments instruments)
            : this(dimensions, GetMeter(instruments))
        {
        }

        private static KeyValuePair<string, object>[] CreateDimensions(BlockPoolMonitorDimensions dimensions)
        {
            ArgumentNullException.ThrowIfNull(dimensions);
            return [new("BlockPoolId", dimensions.BlockPoolId)];
        }

        private static Meter GetMeter(OrleansInstruments instruments)
        {
            ArgumentNullException.ThrowIfNull(instruments);
            return instruments.Meter;
        }

        private DefaultBlockPoolMonitor(KeyValuePair<string, object>[] dimensions, Meter meter)
        {
            _dimensions = dimensions;
            _totalMemoryCounter = meter.CreateObservableCounter<long>(InstrumentNames.STREAMS_BLOCK_POOL_TOTAL_MEMORY, GetTotalMemory, unit: "bytes");
            _availableMemoryCounter = meter.CreateObservableCounter<long>(InstrumentNames.STREAMS_BLOCK_POOL_AVAILABLE_MEMORY, GetAvailableMemory, unit: "bytes");
            _claimedMemoryCounter = meter.CreateObservableCounter<long>(InstrumentNames.STREAMS_BLOCK_POOL_CLAIMED_MEMORY, GetClaimedMemory, unit: "bytes");
            _releasedMemoryCounter = meter.CreateObservableCounter<long>(InstrumentNames.STREAMS_BLOCK_POOL_RELEASED_MEMORY, GetReleasedMemory, unit: "bytes");
            _allocatedMemoryCounter = meter.CreateObservableCounter<long>(InstrumentNames.STREAMS_BLOCK_POOL_ALLOCATED_MEMORY, GetAllocatedMemory, unit: "bytes");
        }

        // _dimensions 中的标记值实际上从不为 null；此转换仅放宽数组
        // 元素类型的可空性注解，以满足下面带可空性注解的 System.Diagnostics.Metrics.Measurement<T> 构造函数的要求。
        // 这是安全的，因为无论元素的可空性注解如何，底层 CLR 数组类型都相同。
        private Measurement<long> GetTotalMemory() => new(_totalMemory, (KeyValuePair<string, object?>[])(object)_dimensions);
        private Measurement<long> GetAvailableMemory() => new(_availableMemory, (KeyValuePair<string, object?>[])(object)_dimensions);
        private Measurement<long> GetClaimedMemory() => new(_claimedMemory, (KeyValuePair<string, object?>[])(object)_dimensions);
        private Measurement<long> GetReleasedMemory() => new(_releasedMemory, (KeyValuePair<string, object?>[])(object)_dimensions);
        private Measurement<long> GetAllocatedMemory() => new(_allocatedMemory, (KeyValuePair<string, object?>[])(object)_dimensions);

        /// <inheritdoc />
        public void Report(long totalMemoryInByte, long availableMemoryInByte, long claimedMemoryInByte)
        {
            _totalMemory = totalMemoryInByte;
            _availableMemory = availableMemoryInByte;
            _claimedMemory = claimedMemoryInByte;
        }

        /// <inheritdoc />
        public void TrackMemoryReleased(long releasedMemoryInByte)
        {
            Interlocked.Add(ref _releasedMemory, releasedMemoryInByte);
        }

        /// <inheritdoc />
        public void TrackMemoryAllocated(long allocatedMemoryInByte)
        {
            Interlocked.Add(ref _allocatedMemory, allocatedMemoryInByte);
        }
    }
}
