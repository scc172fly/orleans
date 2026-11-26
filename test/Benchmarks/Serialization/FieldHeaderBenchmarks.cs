using BenchmarkDotNet.Attributes;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;
using Microsoft.Extensions.DependencyInjection;
using Benchmarks.Serialization.Utilities;

namespace Benchmarks.Serialization;

/// <summary>
/// 基准 Orleans 有线协议外地页头编码和写作性能。
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

        // 使用预期类型和字段代号,其值小到足以嵌入。
        writer.WriteFieldHeader(4, typeof(uint), typeof(uint), WireType.VarInt);
    }

    [Benchmark]
    public void WritePlainExpectedExtendedId()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用字段 id 三角洲,该字段太大,无法嵌入。
        writer.WriteFieldHeader(Tag.MaxEmbeddedFieldIdDelta + 20, typeof(uint), typeof(uint), WireType.VarInt);
    }

    [Benchmark]
    public void WriteFastEmbedded()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用预期类型和字段代号,其值小到足以嵌入。
        writer.WriteFieldHeaderExpected(4, WireType.VarInt);
    }

    [Benchmark]
    public void WriteFastExtended()
    {
        var writer = new SingleSegmentBuffer(OrleansBuffer).CreateWriter(Session);

        // 使用字段 id 三角洲,该字段太大,无法嵌入。
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
