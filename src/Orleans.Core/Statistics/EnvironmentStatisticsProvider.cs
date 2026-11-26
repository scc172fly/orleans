using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using Orleans.Runtime;

namespace Orleans.Statistics;

internal sealed class EnvironmentStatisticsProvider : IEnvironmentStatisticsProvider, IDisposable
{
    private const float OneKiloByte = 1024f;
    private static readonly IMeterFactory MeterFactory = new DirectMeterFactory();

    private long _availableMemoryBytes;
    private long _maximumAvailableMemoryBytes;

    private readonly EventCounterListener _eventCounterListener = new();

    [SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Used for memory-dump debugging.")]
    private readonly ObservableGauge<long> _availableMemoryGauge;

    [SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "Used for memory-dump debugging.")]
    private readonly ObservableGauge<long> _maximumAvailableMemoryGauge;

    private readonly DualModeKalmanFilter _cpuUsageFilter = new();
    private readonly DualModeKalmanFilter _memoryUsageFilter = new();

    public EnvironmentStatisticsProvider()
        : this(new OrleansInstruments(MeterFactory))
    {
    }

    public EnvironmentStatisticsProvider(OrleansInstruments instruments)
        : this(instruments.Meter)
    {
    }

    private EnvironmentStatisticsProvider(Meter meter)
    {
        GC.Collect(0, GCCollectionMode.Forced, true); // 我们确保GC结构不会空, 并履行封锁GC保证立即收取。

        _availableMemoryGauge = meter.CreateObservableGauge(InstrumentNames.RUNTIME_MEMORY_AVAILABLE_MEMORY_MB, () => (long)(_availableMemoryBytes / OneKiloByte / OneKiloByte), unit: "MB");
        _maximumAvailableMemoryGauge = meter.CreateObservableGauge(InstrumentNames.RUNTIME_MEMORY_TOTAL_PHYSICAL_MEMORY_MB, () => (long)(_maximumAvailableMemoryBytes / OneKiloByte / OneKiloByte), unit: "MB");
    }

    /// <inheritdoc />
    public EnvironmentStatistics GetEnvironmentStatistics()
    {
        var cpuUsage = _eventCounterListener.CpuUsage;

        var memoryInfo = GC.GetGCMemoryInfo();
        var maximumAvailableMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;
        var memoryUsage = Math.Clamp(memoryInfo.TotalCommittedBytes - memoryInfo.FragmentedBytes, 0, maximumAvailableMemoryBytes);
        var availableMemory = maximumAvailableMemoryBytes - memoryUsage;
        var filteredCpuUsage = _cpuUsageFilter.Filter(cpuUsage);
        var filteredMemoryUsage = (long)_memoryUsageFilter.Filter(memoryUsage);
        var filteredAvailableMemory = maximumAvailableMemoryBytes - filteredMemoryUsage;

        var result = new EnvironmentStatistics(
            cpuUsagePercentage: filteredCpuUsage,
            rawCpuUsagePercentage: cpuUsage,
            memoryUsageBytes: filteredMemoryUsage,
            rawMemoryUsageBytes: memoryUsage,
            availableMemoryBytes: filteredAvailableMemory,
            rawAvailableMemoryBytes: availableMemory,
            maximumAvailableMemoryBytes: maximumAvailableMemoryBytes);

        _maximumAvailableMemoryBytes = result.MaximumAvailableMemoryBytes;
        _availableMemoryBytes = result.RawAvailableMemoryBytes;
        return result;
    }

    public void Dispose() => _eventCounterListener.Dispose();

    private sealed class EventCounterListener : EventListener
    {
        public float CpuUsage { get; private set; }

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name.Equals("System.Runtime", StringComparison.Ordinal))
            {
                Dictionary<string, string?>? refreshInterval = new() { ["EventCounterIntervalSec"] = "1" };
                EnableEvents(source, EventLevel.Informational, (EventKeywords)(-1), refreshInterval);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if ("EventCounters".Equals(eventData.EventName, StringComparison.Ordinal) && eventData.Payload is { } payload)
            {
                for (var i = 0; i < payload.Count; i++)
                {
                    if (payload[i] is IDictionary<string, object?> eventPayload
                        && eventPayload.TryGetValue("Name", out var name)
                        && "cpu-usage".Equals(name)
                        && eventPayload.TryGetValue("Mean", out var mean)
                        && mean is double value)
                    {
                        CpuUsage = Math.Clamp((float)value, 0f, 100f);
                        break;
                    }
                }
            }
        }
    }

    private sealed class DirectMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }

    // 见: https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/

    // 使用合作双模式KF的理由是我们希望输入信号沿着
    // 与原信标速度相比,衰变速度慢于原信标的速度,但一旦信号增加,也会跟踪信号
    // 两者都很重要,但它们彼此之间却反相相关。
    private sealed class DualModeKalmanFilter
    {
        private const float SlowProcessNoiseCovariance = 0f;
        private const float FastProcessNoiseCovariance = 0.01f;

        private KalmanFilter _slowFilter = new();
        private KalmanFilter _fastFilter = new();

        private FilterRegime _regime = FilterRegime.Slow;

        private enum FilterRegime
        {
            Slow,
            Fast
        }

        public float Filter(float measurement)
        {
            float slowEstimate = _slowFilter.Filter(measurement, SlowProcessNoiseCovariance);
            float fastEstimate = _fastFilter.Filter(measurement, FastProcessNoiseCovariance);

            if (measurement > slowEstimate)
            {
                if (_regime == FilterRegime.Slow)
                {
                    _regime = FilterRegime.Fast;
                    _fastFilter.SetState(measurement, 0f);
                    fastEstimate = _fastFilter.Filter(measurement, FastProcessNoiseCovariance);
                }

                return fastEstimate;
            }
            else
            {
                if (_regime == FilterRegime.Fast)
                {
                    _regime = FilterRegime.Slow;
                    _slowFilter.SetState(_fastFilter.PriorEstimate, _fastFilter.PriorErrorCovariance);
                    slowEstimate = _slowFilter.Filter(measurement, SlowProcessNoiseCovariance);
                }

                return slowEstimate;
            }
        }

        private struct KalmanFilter()
        {
            public float PriorEstimate { get; private set; } = 0f;
            public float PriorErrorCovariance { get; private set; } = 1f;

            public void SetState(float estimate, float errorCovariance)
            {
                PriorEstimate = estimate;
                PriorErrorCovariance = errorCovariance;
            }

            public float Filter(float measurement, float processNoiseCovariance)
            {
                float estimate = PriorEstimate;
                float errorCovariance = PriorErrorCovariance + processNoiseCovariance;

                float gain = errorCovariance / (errorCovariance + 1f);
                float newEstimate = estimate + gain * (measurement - estimate);
                float newErrorCovariance = (1f - gain) * errorCovariance;

                PriorEstimate = newEstimate;
                PriorErrorCovariance = newErrorCovariance;

                return newEstimate;
            }
        }
    }
}
