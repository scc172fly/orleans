using BenchmarkDotNet.Attributes;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;
using Microsoft.Extensions.DependencyInjection;
using Benchmarks.Serialization.Utilities;

namespace Benchmarks.Serialization;

/// <summary>
/// 对 Orleans 传输协议的字段头编码及写入性能进行基准测试。
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class FieldHeaderBenchmarks
{
    private static readonly SerializerSession Session;
    private static readonly byte[] OrleansBuffer = new byte[1000];

    static FieldHeaderBenchmarks()
    {
        var services = new ServiceCollection().AddSerializer();
        var serviceProvider = services.BuildServiceProvider();
        var sessionPool = serviceProvider.GetRequiredService<SerializerSessionPool>();
        Session = sessionPool.GetSession();
    }

    [Benchmark(Baseline = true)]
    public void WritePlainExpectedEmbeddedId()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用预期类型，以及数值足够小、可嵌入字段头的字段 ID。
        writer.WriteFieldHeader(4, typeof(uint), typeof(uint), WireType.VarInt);
    }

    [Benchmark]
    public void WritePlainExpectedExtendedId()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用数值过大、无法嵌入字段头的字段 ID 增量。
        writer.WriteFieldHeader(Tag.MaxEmbeddedFieldIdDelta + 20, typeof(uint), typeof(uint), WireType.VarInt);
    }

    [Benchmark]
    public void WriteFastEmbedded()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用预期类型，以及数值足够小、可嵌入字段头的字段 ID。
        writer.WriteFieldHeaderExpected(4, WireType.VarInt);
    }

    [Benchmark]
    public void WriteFastExtended()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用数值过大、无法嵌入字段头的字段 ID 增量。
        writer.WriteFieldHeaderExpected(Tag.MaxEmbeddedFieldIdDelta + 20, WireType.VarInt);
    }

    [Benchmark]
    public void CreateWriter() => _ = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

    [Benchmark]
    public void WriteByte()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);
        writer.WriteByte((byte)4);
    }
}
