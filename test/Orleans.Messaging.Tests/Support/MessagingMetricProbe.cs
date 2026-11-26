using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Xunit;

namespace Orleans.Durable.Messaging.Tests.Support;

public sealed class MessagingMetricProbe : IDisposable
{
    private readonly ConcurrentDictionary<(string Instrument, string TagValue), long> _measurements = [];
    private readonly ConcurrentDictionary<(Meter Meter, string Instrument), long> _depths = [];
    private readonly object _lock = new();
    private TaskCompletionSource _changed = CreateSignal();
    private readonly MeterListener _listener;

    public MessagingMetricProbe()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Microsoft.Orleans"
                    && instrument.Name is "orleans-messaging-orphaned-jobs-reclaimed"
                        or "orleans-messaging-inbox-messages-processed"
                        or "orleans-durablejobs-job-attempts-started"
                        or "orleans-durablejobs-handler-executions-started"
                        or "orleans-durablejobs-jobs-completed"
                        or "orleans-durablejobs-jobs-rescheduled"
                        or "orleans-messaging-inbox-depth"
                        or "orleans-messaging-outbox-depth")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.Start();
    }

    public long GetCount(string instrument, string tagValue = "") =>
        _measurements.TryGetValue((instrument, tagValue), out var count) ? count : 0;

    /// <summary>获取 <paramref name="meter"/>所创建的深度仪器的当前值。</summary>
    /// <remarks>每一 silo 自己创建自己的计量仪, 所以一个计数识别一个 silo深度。</remarks>
    public long GetDepth(Meter meter, string instrument) => _depths.TryGetValue((meter, instrument), out var value) ? value : 0;

    public Task WaitForCountAsync(
        string instrument,
        long expected,
        string tagValue = "") =>
        WaitForCountWithCancellationAsync(
            instrument,
            expected,
            tagValue,
            TestContext.Current.CancellationToken);

    public Task WaitForCountWithCancellationAsync(
        string instrument,
        long expected,
        string tagValue,
        CancellationToken cancellationToken) =>
        WaitUntilAsync(() => GetCount(instrument, tagValue) >= expected, cancellationToken);

    /// <summary>等待到 <paramref name="condition"/>,读本探测器的测量结果,它会保持。</summary>
    public async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            Task changed;
            lock (_lock)
            {
                if (condition())
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(timeout.Token);
        }
    }

    public void Dispose() => _listener.Dispose();

    private void OnMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name is "orleans-messaging-inbox-depth" or "orleans-messaging-outbox-depth")
        {
            // 深度仪器是 UpDownCounters,因此每个测量都是三角洲。
            _depths.AddOrUpdate((instrument.Meter, instrument.Name), measurement, (_, current) => current + measurement);
            SignalChanged();
            return;
        }

        var tagValue = "";
        var tagName = instrument.Name == "orleans-messaging-inbox-messages-processed" ? "status" : "job_name";
        foreach (var tag in tags)
        {
            if (tag.Key == tagName)
            {
                tagValue = tag.Value as string ?? "";
                break;
            }
        }

        _measurements.AddOrUpdate(
            (instrument.Name, tagValue),
            measurement,
            (_, current) => current + measurement);
        SignalChanged();
    }

    private void SignalChanged()
    {
        lock (_lock)
        {
            _changed.TrySetResult();
            _changed = CreateSignal();
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
