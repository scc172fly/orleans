#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TestKit;
using Xunit;

namespace Orleans.Serialization.UnitTests;

/// <summary>
/// Orleans 对 MessagePack 序列化的支持测试。
/// 
/// MessagePack 是一种二进制序列化格式，具有以下特点：
/// - 比 JSON 更紧凑的表示
/// - 比文本格式更快的序列化和反序列化
/// - 支持跨平台和跨语言使用
/// 
/// Orleans 的 MessagePack 集成：
/// - 使用 MessagePack-CSharp 库
/// - 支持联合类型（可辨识联合）
/// - 同时提供序列化和深度复制功能
/// - 可以只将它用于特定类型，其余类型仍使用 Orleans 原生格式
/// 
/// 这对于以下场景很有用：
/// - 与使用 MessagePack 的系统进行互操作
/// - 需要具有广泛语言支持的紧凑二进制格式
/// - 需要比 JSON 更好的性能，同时又比 Orleans 原生格式具有更好的可移植性
/// </summary>
[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class MessagePackCodecTests : FieldCodecTester<MyMessagePackClass?, IFieldCodec<MyMessagePackClass?>>, IClassFixture<SerializationTesterFixture>
{
    public MessagePackCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override void Configure(ISerializerBuilder builder)
    {
        builder.AddMessagePackSerializer();
    }

    protected override MyMessagePackClass? CreateValue() => new() { IntProperty = 30, StringProperty = "hello", SubClass = new() { Id = Guid.NewGuid() } };

    protected override MyMessagePackClass?[] TestValues => new MyMessagePackClass?[]
    {
        null,
        new() { SubClass = new() { Id = Guid.NewGuid() } },
        new() { IntProperty = 150, StringProperty = new string('c', 20), SubClass = new() { Id = Guid.NewGuid() } },
        new() { IntProperty = 150_000, StringProperty = new string('c', 6_000), SubClass = new() { Id = Guid.NewGuid() } },
        new() { Union = new MyMessagePackUnionVariant1 { IntProperty = 1 } },
        new() { Union = new MyMessagePackUnionVariant2 { StringProperty = "String" } },
    };

    [Fact]
    public void MessagePackSerializerDeepCopyTyped()
    {
        var original = new MyMessagePackClass { IntProperty = 30, StringProperty = "hi", SubClass = new() { Id = Guid.NewGuid() } };
        var copier = ServiceProvider.GetRequiredService<DeepCopier<MyMessagePackClass>>();
        var result = copier.Copy(original)!;

        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
        // 两个值均已在上方初始化，复制操作会保留嵌套对象。
        Assert.Equal(original.SubClass!.Id, result.SubClass!.Id);
    }

    [Fact]
    public void MessagePackSerializerDeepCopyUntyped()
    {
        var original = new MyMessagePackClass { IntProperty = 30, StringProperty = "hi", SubClass = new() { Id = Guid.NewGuid() } };
        var copier = ServiceProvider.GetRequiredService<DeepCopier>();
        var result = (MyMessagePackClass)copier.Copy((object)original)!;

        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
        // 两个值均已在上方初始化，复制操作会保留嵌套对象。
        Assert.Equal(original.SubClass!.Id, result.SubClass!.Id);
    }

    [Fact]
    public void MessagePackSerializerRoundTripThroughCodec()
    {
        var original = new MyMessagePackClass { IntProperty = 30, StringProperty = "hi", SubClass = new() { Id = Guid.NewGuid() } };
        var result = RoundTripThroughCodec(original)!;

        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
    }

    [Fact]
    public void MessagePackSerializerRoundTripThroughUntypedSerializer()
    {
        var original = new MyMessagePackClass { IntProperty = 30, StringProperty = "hi", SubClass = new() { Id = Guid.NewGuid() } };
        var untypedResult = RoundTripThroughUntypedSerializer(original, out _);

        var result = Assert.IsType<MyMessagePackClass>(untypedResult);
        Assert.Equal(original.IntProperty, result.IntProperty);
        Assert.Equal(original.StringProperty, result.StringProperty);
    }
}


[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class MessagePackUnionCodecTests : FieldCodecTester<IMyMessagePackUnion?, IFieldCodec<IMyMessagePackUnion?>>, IClassFixture<SerializationTesterFixture>
{
    public MessagePackUnionCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override void Configure(ISerializerBuilder builder)
    {
        builder.AddMessagePackSerializer();
    }

    protected override IMyMessagePackUnion? CreateValue() => new MyMessagePackUnionVariant1() { IntProperty = 30 };

    protected override IMyMessagePackUnion?[] TestValues => new IMyMessagePackUnion?[]
    {
        null,
        new MyMessagePackUnionVariant1 { IntProperty = 1 },
        new MyMessagePackUnionVariant2 { StringProperty = "String" },
    };
}


[Trait("Category", "BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Serialization")]
public class MessagePackCodecCopierTests : CopierTester<MyMessagePackClass?, IDeepCopier<MyMessagePackClass?>>, IClassFixture<SerializationTesterFixture>
{
    public MessagePackCodecCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override void Configure(ISerializerBuilder builder)
    {
        builder.AddMessagePackSerializer();
    }
    protected override IDeepCopier<MyMessagePackClass?> CreateCopier() => ServiceProvider.GetRequiredService<ICodecProvider>().GetDeepCopier<MyMessagePackClass?>();

    protected override MyMessagePackClass? CreateValue() => new() { IntProperty = 30, StringProperty = "hello", SubClass = new() { Id = Guid.NewGuid() } };

    protected override MyMessagePackClass?[] TestValues => new MyMessagePackClass?[]
    {
        null,
        new() { SubClass = new() { Id = Guid.NewGuid() } },
        new() { IntProperty = 150, StringProperty = new string('c', 20), SubClass = new() { Id = Guid.NewGuid() } },
        new() { IntProperty = 150_000, StringProperty = new string('c', 6_000), SubClass = new() { Id = Guid.NewGuid() } },
    };
}
