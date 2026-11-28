#nullable enable
using System;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TestKit;
using Xunit;

namespace Orleans.Serialization.UnitTests;

/// <summary>
/// Orleans 对 Protocol Buffers（protobuf）序列化的支持测试。
/// 
/// Protocol Buffers 是 Google 的一种与语言和平台无关的序列化格式，提供：
/// - 紧凑的二进制编码
/// - 通过 .proto 文件提供严格的架构定义
/// - 出色的跨语言支持
/// - 向前和向后兼容性
/// 
/// Orleans 的 protobuf 集成：
/// - 支持 Google.Protobuf 生成的类
/// - 处理 protobuf 集合及嵌套消息
/// - 同时提供序列化和深度复制功能
/// - 与 Orleans 类型系统集成以支持多态
/// 
/// 此集成对于以下场景很有价值：
/// - 与 gRPC 服务对接
/// - 与非 .NET 系统共享数据
/// - 需要架构优先的开发方式
/// - 需要高度优化的传输格式
/// </summary>
[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class ProtobufSerializerTests : FieldCodecTester<MyProtobufClass?, IFieldCodec<MyProtobufClass?>>, IClassFixture<SerializationTesterFixture>
{
    public ProtobufSerializerTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override void Configure(ISerializerBuilder builder)
    {
        builder.AddProtobufSerializer();
    }

    protected override MyProtobufClass? CreateValue() => new() { IntProperty = 30, StringProperty = "hello", SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } };

    protected override MyProtobufClass?[] TestValues => new MyProtobufClass?[]
    {
        null,
        new () { SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } },
        new () { IntProperty = 150, StringProperty = new string('c', 20), SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } },
        new () { IntProperty = -150_000, StringProperty = new string('c', 6_000), SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } },
    };

    [Fact]
    public void ProtobufSerializerDeepCopyTyped()
    {
        var original = new MyProtobufClass { IntProperty = 30, StringProperty = "hi", SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } };
        var copier = ServiceProvider.GetRequiredService<DeepCopier<MyProtobufClass>>();
        var result = copier.Copy(original)!;

        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
    }

    [Fact]
    public void ProtobufSerializerDeepCopyUntyped()
    {
        var original = new MyProtobufClass { IntProperty = 30, StringProperty = "hi", SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } };
        var copier = ServiceProvider.GetRequiredService<DeepCopier>();
        var result = (MyProtobufClass)copier.Copy((object)original)!;

        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
    }

    [Fact]
    public void ProtobufSerializerRoundTripThroughCodec()
    {
        var original = new MyProtobufClass { IntProperty = 30, StringProperty = "hi", SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } };
        var result = RoundTripThroughCodec(original)!;

        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
    }

    [Fact]
    public void ProtobufSerializerRoundTripThroughUntypedSerializer()
    {
        var original = new MyProtobufClass { IntProperty = 30, StringProperty = "hi", SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } };
        var untypedResult = RoundTripThroughUntypedSerializer(original, out _);

        var result = Assert.IsType<MyProtobufClass>(untypedResult);
        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
    }
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class ProtobufCodecCopierTests : CopierTester<MyProtobufClass?, IDeepCopier<MyProtobufClass?>>, IClassFixture<SerializationTesterFixture>
{
    public ProtobufCodecCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override void Configure(ISerializerBuilder builder)
    {
        builder.AddProtobufSerializer();
    }
    protected override IDeepCopier<MyProtobufClass?> CreateCopier() => ServiceProvider.GetRequiredService<ICodecProvider>().GetDeepCopier<MyProtobufClass?>();

    protected override MyProtobufClass? CreateValue() => new MyProtobufClass { IntProperty = 30, StringProperty = "hello", SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } };

    protected override MyProtobufClass?[] TestValues => new MyProtobufClass?[]
    {
        null,
        new () { SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } },
        new () { IntProperty = 150, StringProperty = new string('c', 20), SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } },
        new () { IntProperty = -150_000, StringProperty = new string('c', 6_000), SubClass = new MyProtobufClass.Types.SubClass { Id = Guid.NewGuid().ToByteString() } },
    };
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class ProtobufRepeatedFieldCodecTests : FieldCodecTester<RepeatedField<int>, RepeatedFieldCodec<int>>, IClassFixture<SerializationTesterFixture>
{
    public ProtobufRepeatedFieldCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override RepeatedField<int> CreateValue()
    {
        var result = new RepeatedField<int>();
        for (var i = 0; i < Random.Next(17) + 5; i++)
        {
            result.Add(Random.Next());
        }

        return result;
    }

    protected override bool Equals(RepeatedField<int>? left, RepeatedField<int>? right) => object.ReferenceEquals(left, right) || left!.SequenceEqual(right!);
    protected override RepeatedField<int>[] TestValues => new[] { new RepeatedField<int>(), CreateValue(), CreateValue(), CreateValue() };
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class ProtobufRepeatedFieldCopierTests : CopierTester<RepeatedField<int>, IDeepCopier<RepeatedField<int>>>, IClassFixture<SerializationTesterFixture>
{
    public ProtobufRepeatedFieldCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override IDeepCopier<RepeatedField<int>> CreateCopier() => ServiceProvider.GetRequiredService<ICodecProvider>().GetDeepCopier<RepeatedField<int>>();

    protected override RepeatedField<int> CreateValue()
    {
        var result = new RepeatedField<int>();
        for (var i = 0; i < Random.Next(17) + 5; i++)
        {
            result.Add(Random.Next());
        }

        return result;
    }

    protected override bool Equals(RepeatedField<int>? left, RepeatedField<int>? right) => object.ReferenceEquals(left, right) || left!.SequenceEqual(right!);
    protected override RepeatedField<int>[] TestValues => new[] { new RepeatedField<int>(), CreateValue(), CreateValue(), CreateValue() };
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class MapFieldCodecTests : FieldCodecTester<MapField<string, int>, MapFieldCodec<string, int>>, IClassFixture<SerializationTesterFixture>
{
    public MapFieldCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MapField<string, int> CreateValue()
    {
        var result = new MapField<string, int>();
        for (var i = 0; i < Random.Next(17) + 5; i++)
        {
            result[Random.Next().ToString()] = Random.Next();
        }

        return result;
    }

    protected override MapField<string, int>[] TestValues => new[] { new MapField<string, int>(), CreateValue(), CreateValue(), CreateValue() };
    protected override bool Equals(MapField<string, int>? left, MapField<string, int>? right) => object.ReferenceEquals(left, right) || left!.SequenceEqual(right!);
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class MapFieldCopierTests : CopierTester<MapField<string, int>, MapFieldCopier<string, int>>, IClassFixture<SerializationTesterFixture>
{
    public MapFieldCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MapField<string, int> CreateValue()
    {
        var result = new MapField<string, int>();
        for (var i = 0; i < Random.Next(17) + 5; i++)
        {
            result[Random.Next().ToString()] = Random.Next();
        }

        return result;
    }

    protected override MapField<string, int>[] TestValues => new[] { new MapField<string, int>(), CreateValue(), CreateValue(), CreateValue() };
    protected override bool Equals(MapField<string, int>? left, MapField<string, int>? right) => object.ReferenceEquals(left, right) || left!.SequenceEqual(right!);
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class ByteStringCodecTests : FieldCodecTester<ByteString, ByteStringCodec>, IClassFixture<SerializationTesterFixture>
{
    public ByteStringCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override ByteString CreateValue() => Guid.NewGuid().ToByteString();

    protected override bool Equals(ByteString? left, ByteString? right) => ReferenceEquals(left, right) || left!.SequenceEqual(right!);

    protected override ByteString[] TestValues => new[]
    {
        ByteString.Empty,
        ByteString.CopyFrom(Enumerable.Range(0, 4097).Select(b => unchecked((byte)b)).ToArray()),
        CreateValue()
    };
}

[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class ByteStringCopierTests : CopierTester<ByteString, ByteStringCopier>, IClassFixture<SerializationTesterFixture>
{
    public ByteStringCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override ByteString CreateValue() => Guid.NewGuid().ToByteString();

    protected override bool Equals(ByteString? left, ByteString? right) => ReferenceEquals(left, right) || left!.SequenceEqual(right!);

    protected override ByteString[] TestValues => new[]
    {
        ByteString.Empty,
        ByteString.CopyFrom(Enumerable.Range(0, 4097).Select(b => unchecked((byte)b)).ToArray()),
        CreateValue()
    };
}

public static class ProtobufGuidExtensions
{
    public static ByteString ToByteString(this Guid guid)
    {
        Span<byte> span = stackalloc byte[16];
        guid.TryWriteBytes(span);
        return ByteString.CopyFrom(span);
    }
}
