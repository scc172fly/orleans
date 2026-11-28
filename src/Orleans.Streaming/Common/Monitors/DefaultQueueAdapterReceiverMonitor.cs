using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using Orleans.Runtime;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// GeneratorStreamProvider 和 MemoryStreamProvider 默认使用的队列适配器接收器监视器。
    /// </summary>
    public class DefaultQueueAdapterReceiverMonitor : IQueueAdapterReceiverMonitor
    {
        private readonly Counter<long> _initializationFailureCounter;
        private readonly Counter<long> _initializationCallTimeCounter;
        private readonly Counter<long> _initializationExceptionCounter;
        private readonly Counter<long> _readFailureCounter;
        private readonly Counter<long> _readCallTimeCounter;
        private readonly Counter<long> _readExceptionCounter;
        private readonly Counter<long> _shutdownFailureCounter;
        private readonly Counter<long> _shutdownCallTimeCounter;
        private readonly Counter<long> _shutdownExceptionCounter;
        private readonly ObservableCounter<long> _messagesReceivedCounter;
        private readonly ObservableGauge<long> _oldestMessageReadEnqueueTimeToNowCounter;
        private readonly ObservableGauge<long> _newestMessageReadEnqueueTimeToNowCounter;
        private readonly KeyValuePair<string, object?>[] _dimensions;
        private ValueStopwatch _oldestMessageReadEnqueueAge;
        private ValueStopwatch _newestMessageReadEnqueueAge;
        private long _messagesReceived;

        /// <summary>
        /// 初始化 <see cref="DefaultQueueAdapterReceiverMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">附加到此监视器报告的指标上的维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        protected DefaultQueueAdapterReceiverMonitor(KeyValuePair<string, object>[] dimensions, OrleansInstruments instruments)
            : this(dimensions, GetMeter(instruments))
        {
        }

        private DefaultQueueAdapterReceiverMonitor(KeyValuePair<string, object>[] dimensions, Meter meter)
        {
            // 此处的标记值实际上从不为 null；此转换仅放宽数组元素
            // 类型的可空性注解，以满足下面带可空性注解的 System.Diagnostics.Metrics API 的要求。
            // 这是安全的，因为无论元素的可空性注解如何，底层 CLR 数组类型都相同。
            _dimensions = (KeyValuePair<string, object?>[])(object)dimensions;
            _initializationFailureCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_INITIALIZATION_FAILURES);
            _initializationCallTimeCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_INITIALIZATION_DURATION);
            _initializationExceptionCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_INITIALIZATION_EXCEPTIONS);

            _readFailureCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_READ_FAILURES);
            _readCallTimeCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_READ_DURATION);
            _readExceptionCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_READ_EXCEPTIONS);

            _shutdownFailureCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_SHUTDOWN_FAILURES);
            _shutdownCallTimeCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_SHUTDOWN_DURATION);
            _shutdownExceptionCounter = meter.CreateCounter<long>(InstrumentNames.STREAMS_QUEUE_SHUTDOWN_EXCEPTIONS);

            _messagesReceivedCounter = meter.CreateObservableCounter<long>(InstrumentNames.STREAMS_QUEUE_MESSAGES_RECEIVED, GetMessagesReceivedCount);
            _oldestMessageReadEnqueueTimeToNowCounter = meter.CreateObservableGauge<long>(InstrumentNames.STREAMS_QUEUE_OLDEST_MESSAGE_ENQUEUE_AGE, GetOldestMessageReadEnqueueAge);
            _newestMessageReadEnqueueTimeToNowCounter = meter.CreateObservableGauge<long>(InstrumentNames.STREAMS_QUEUE_NEWEST_MESSAGE_ENQUEUE_AGE, GetNewestMessageReadEnqueueAge);
        }

        /// <summary>
        /// 初始化 <see cref="DefaultQueueAdapterReceiverMonitor"/> 类的新实例。
        /// </summary>
        /// <param name="dimensions">接收器指标维度。</param>
        /// <param name="instruments">Orleans 运行时度量工具。</param>
        public DefaultQueueAdapterReceiverMonitor(ReceiverMonitorDimensions dimensions, OrleansInstruments instruments)
            : this(CreateDimensions(dimensions), GetMeter(instruments))
        {
        }

        private static KeyValuePair<string, object>[] CreateDimensions(ReceiverMonitorDimensions dimensions)
        {
            ArgumentNullException.ThrowIfNull(dimensions);
            return [new("QueueId", dimensions.QueueId)];
        }

        private static Meter GetMeter(OrleansInstruments instruments)
        {
            ArgumentNullException.ThrowIfNull(instruments);
            return instruments.Meter;
        }

        private Measurement<long> GetOldestMessageReadEnqueueAge() => new(_oldestMessageReadEnqueueAge.ElapsedTicks, _dimensions);
        private Measurement<long> GetNewestMessageReadEnqueueAge() => new(_newestMessageReadEnqueueAge.ElapsedTicks, _dimensions);
        private Measurement<long> GetMessagesReceivedCount() => new(_messagesReceived, _dimensions);

        /// <inheritdoc />
        public void TrackInitialization(bool success, TimeSpan callTime, Exception? exception)
        {
            _initializationFailureCounter.Add(success ? 0 : 1, _dimensions);
            _initializationCallTimeCounter.Add(callTime.Ticks, _dimensions);
            _initializationExceptionCounter.Add(exception is null ? 0 : 1, _dimensions);
        }

        /// <inheritdoc />
        public void TrackRead(bool success, TimeSpan callTime, Exception? exception)
        {
            _readFailureCounter.Add(success ? 0 : 1, _dimensions);
            _readCallTimeCounter.Add(callTime.Ticks, _dimensions);
            _readExceptionCounter.Add(exception is null ? 0 : 1, _dimensions);
        }

        /// <inheritdoc />
        public void TrackMessagesReceived(long count, DateTime? oldestMessageEnqueueTimeUtc, DateTime? newestMessageEnqueueTimeUtc)
        {
            var now = DateTime.UtcNow;
            Interlocked.Add(ref _messagesReceived, count);
            if (oldestMessageEnqueueTimeUtc.HasValue)
            {
                var delta = now - oldestMessageEnqueueTimeUtc.Value;
                _oldestMessageReadEnqueueAge = ValueStopwatch.StartNew(delta);
            }

            if (newestMessageEnqueueTimeUtc.HasValue)
            {
                var delta = now - newestMessageEnqueueTimeUtc.Value;
                _newestMessageReadEnqueueAge = ValueStopwatch.StartNew(delta);
            }
        }

        /// <inheritdoc />
        public void TrackShutdown(bool success, TimeSpan callTime, Exception? exception)
        {
            _shutdownFailureCounter.Add(success ? 0 : 1, _dimensions);
            _shutdownCallTimeCounter.Add(callTime.Ticks, _dimensions);
            _shutdownExceptionCounter.Add(exception is null ? 0 : 1, _dimensions);
        }
    }
}
