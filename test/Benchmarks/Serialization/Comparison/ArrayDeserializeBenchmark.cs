using System.Buffers;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Benchmarks.Serialization.Models;
using Benchmarks.Serialization.Utilities;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Benchmarks.Serialization.Comparison;

#pragma warning disable IDE1006 // Naming Styles
/// <summary>
/// 比较 Orleans 与其他常用序列化器对数组类型的反序列化性能。
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Serialization")]
public class ArrayDeserializeBenchmark
{
    private static readonly MyVector3[] _value;
    private static readonly Serializer<MyVector3[]> _orleansSerializer;
    private static readonly byte[] _stjPayload;
    private static readonly byte[] _protobufPayload;
    private static readonly byte[] _orleansPayload;
    private static readonly byte[] _messagePackPayload;

    static ArrayDeserializeBenchmark()
    {
        _value = Enumerable.Repeat(new MyVector3 { X = 10.3f, Y = 40.5f, Z = 13411.3f }, 1000).ToArray();
        var serviceProvider = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        _orleansSerializer = serviceProvider.GetRequiredService<Serializer<MyVector3[]>>();


        _orleansPayload = _orleansSerializer.SerializeToArray(_value);
        _messagePackPayload = MessagePackSerializer.Serialize(_value);

        var stream = new MemoryStream();
        ProtoBuf.Serializer.Serialize(stream, _value);
        stream.Position = 0;
        _protobufPayload = stream.ToArray();
        _stjPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_value));
    }

    [Benchmark(Baseline = true)]
    public MyVector3[] MessagePackDeserialize() => MessagePackSerializer.Deserialize<MyVector3[]>(_messagePackPayload);

    [Benchmark]
    public MyVector3[] ProtobufNetDeserialize() => ProtoBuf.Serializer.Deserialize<MyVector3[]>(_protobufPayload.AsSpan());

    [Benchmark]
    public MyVector3[] SystemTextJsonDeserialize() => JsonSerializer.Deserialize<MyVector3[]>(_stjPayload)!;

    [Benchmark]
    public MyVector3[] OrleansDeserialize() => _orleansSerializer.Deserialize(_orleansPayload)!; // 初始化期间，使用 MyVector3 数组序列化生成有效载荷。
}
#pragma warning restore IDE1006 // Naming Styles
