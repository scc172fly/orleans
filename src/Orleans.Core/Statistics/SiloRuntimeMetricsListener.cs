
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace Orleans.Runtime;

// 无法使用 MetricsEventSource , 因为它只支持单个听众 。
/// <summary>
/// 听 Orleans 用于生产 silo 运行时统计。
/// </summary>
/// <remarks>
/// 收集 <c>orleans-messaging-sent-messages-size</c> 和 <c>orleans-messaging-received-messages-size</c>
/// 自收集开始以来,通过 OpenTelemetry 获得电文字节总计的直方图。
/// </remarks>
[Obsolete("Use the orleans-messaging-sent-messages-size and orleans-messaging-received-messages-size histograms through OpenTelemetry instead.")]
public static class SiloRuntimeMetricsListener
{
    private static readonly MeterListener MeterListener = new();

    private static long _connectedClientCount;

    /// <summary>
    /// 获取当前连接到 silo的客户数量。
    /// </summary>
    public static long ConnectedClientCount => _connectedClientCount;

    private static long _messageReceivedTotal;

    /// <summary>
    /// 获取 silo收到的信件的累积大小,以字节计。
    /// </summary>
    public static long MessageReceivedTotal => _messageReceivedTotal;

    private static long _messageSentTotal;

    /// <summary>
    /// 获取 silo发送的信件的累积大小,以字节计。
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
