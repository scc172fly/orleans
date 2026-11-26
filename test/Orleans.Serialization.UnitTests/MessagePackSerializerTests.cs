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
/// Orleans' MessagePack 序列化支持测试。
/// 
/// MessagePack 是一个二进制序列化格式,提供:
/// - 代表比 JSON更紧凑
/// - 与文本格式相比,更快的序列/删除
/// - 跨平台和跨语文支助
/// 
/// Orleans' MessagePack 整合:
/// - 利用 MessagePack- Charp 库
/// - 支持工会类型(受歧视的工会)
/// - 提供序列和深复制功能
/// - 可用于特定类型,而使用 Orleans原本格式用于其他
/// 
/// 这一点在下列情况下有用:
/// - 与使用 MessagePack系统的相互操作
/// - 要求一种具有广泛语言支持的缩写二元格式
/// - 需要比 JSON 更好的性能,但比 Orleans本型更便于携带
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
        // 以上两个数值初始化,复制保留了嵌套对象。
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
        // 以上两个数值初始化,复制保留了嵌套对象。
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
