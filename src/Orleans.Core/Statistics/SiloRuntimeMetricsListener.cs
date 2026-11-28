
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace Orleans.Runtime;

// 无法使用 MetricsEventSource，因为它仅支持单个监听器。
/// <summary>
/// 监听用于生成 Silo 运行时统计信息的 Orleans 运行时指标。
/// </summary>
/// <remarks>
/// 通过 OpenTelemetry 收集 <c>orleans-messaging-sent-messages-size</c> 和 <c>orleans-messaging-received-messages-size</c>
/// 直方图，以获取自开始收集以来的消息字节总数。
/// </remarks>
[Obsolete("Use the orleans-messaging-sent-messages-size and orleans-messaging-received-messages-size histograms through OpenTelemetry instead.")]
public static class SiloRuntimeMetricsListener
{
    private static readonly MeterListener MeterListener = new();

    private static long _connectedClientCount;

    /// <summary>
    /// 获取当前连接到 Silo 的客户端数量。
    /// </summary>
    public static long ConnectedClientCount => _connectedClientCount;

    private static long _messageReceivedTotal;

    /// <summary>
    /// 获取 Silo 已接收消息的累计大小（以字节为单位）。
    /// </summary>
    public static long MessageReceivedTotal => _messageReceivedTotal;

    private static long _messageSentTotal;

    /// <summary>
    /// 获取 Silo 已发送消息的累计大小（以字节为单位）。
    /// </summary>
    public static long MessageSentTotal => _messageSentTotal;

    private static readonly string[] MetricNames =
    {
        // orleans
        InstrumentNames.GATEWAY_CONNECTED_CLIENTS,
        InstrumentNames.MESSAGING_RECEIVED_MESSAGES_SIZE,
        InstrumentNames.MESSAGING_SENT_MESSAGES_SIZE,
    };

    static SiloRuntimeMetricsListener()
    {
        MeterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (MetricNames.Contains(instrument.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        MeterListener.SetMeasurementEventCallback<int>(OnMeasurementRecorded);
    }

    internal static void Start()
    {
        MeterListener.Start();
    }

    private static void OnMeasurementRecorded(Instrument instrument, int measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (instrument.Name == InstrumentNames.GATEWAY_CONNECTED_CLIENTS)
        {
            Interlocked.Add(ref _connectedClientCount, measurement);
        }
        if (instrument.Name == InstrumentNames.MESSAGING_RECEIVED_MESSAGES_SIZE)
        {
            Interlocked.Add(ref _messageReceivedTotal, measurement);
        }
        if (instrument.Name == InstrumentNames.MESSAGING_SENT_MESSAGES_SIZE)
        {
            Interlocked.Add(ref _messageSentTotal, measurement);
        }
    }
}
