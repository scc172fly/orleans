#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orleans.Serialization.UnitTests;

/// <summary>
/// C# 记录类型 Orleans支持的测试。
/// 
/// 记录是一流公民的 Orleans 序列化记录,充分支持:
/// - 初级建筑参数
/// - 仅使用 Init 属性
/// - 记录继承等级制度
/// - 基本类型简要记录摘要
/// - 复杂的记录结构,具有多个继承等级
/// 
/// Orleans 高效率地处理记录,具体做法是:
/// - 产生最优化的序列器,能理解记录语义
/// - 维护价值平等语义
/// - 支持上下上下记录语法和名义上记录词法
/// - 处理记录支架(价值类型记录)
/// 
/// 这种支持对于现代C#应用程序至关重要,因为这些应用可以将记录用于:
/// - 数据模型不可变
/// - 带有价值对象的域驱动设计
/// - 职能方案拟订模式
/// - 清洁 API 合同
/// </summary>
[Trait("Category", "BVT")]
[Trait("Suite", "BVT")]
[Trait("Provider", "None")]
[Trait("Area", "Serialization")]
public class RecordSerializationTests
{
    private readonly ServiceProvider _services;
    private readonly Serializer _serializer;

    public RecordSerializationTests()
    {
        _services = new ServiceCollection()
            .AddSerializer()
            .BuildServiceProvider();
        _serializer = _services.GetRequiredService<Serializer>();
    }

    [Fact]
    public void CanSerializeEmptyAbstractRecord()
    {
        var serializer = _services.GetRequiredService<Serializer<DerivedFromEmptyAbstractRecord>>();

        var key = new DerivedFromEmptyAbstractRecord("Sample Foo");
        var bytes = serializer.SerializeToArray(key);
        var newKey = serializer.Deserialize(bytes);

        Assert.Equal(key, newKey);
    }

    [Fact]
    public void CanSerializePopulatedAbstractRecord()
    {
        var serializer = _services.GetRequiredService<Serializer<DerivedFromNonEmptyAbstractRecord>>();

        var key = new DerivedFromNonEmptyAbstractRecord("Sample Foo");
        var bytes = serializer.SerializeToArray(key);
        var newKey = serializer.Deserialize(bytes);

        Assert.Equal(key, newKey);
    }

    [Fact]
    public void CanSerializeRecordsWithEmptyHierarchyLayers()
    {
        var serializer = _services.GetRequiredService<Serializer<RecordHierarchyDerived>>();

        var expected = new RecordHierarchyDerived(Message: "test");
        var bytes = serializer.SerializeToArray(expected);
        var result = serializer.Deserialize(bytes);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CanSerializeRecordsInList()
    {
        var serializer = _services.GetRequiredService<Serializer>();
        var expected = new List<DerivedFromNonEmptyAbstractRecord>
        {
            new DerivedFromNonEmptyAbstractRecord("foo") { Bar = "bar" },
            new DerivedFromNonEmptyAbstractRecord("foo2") { Bar = "bar2" },
        };
        var bytes = serializer.SerializeToArray(expected);
        var result = serializer.Deserialize<List<DerivedFromNonEmptyAbstractRecord>>(bytes);
        Assert.NotNull(result);
        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected[0], result[0]);
        Assert.Equal(expected[1], result[1]);
    }

    [Fact]
    public void Can_Roundtrip_WithListOfObject_Fruit()
    {
        var original = new FooWithListOfObject
        {
            Items = new List<object> { new FruitRecord("Banana"), new FruitRecord("Mango") },
            Bar = new FooRecord(Guid.NewGuid())
        };

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FooWithListOfObject>(bytes);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Bar);
        Assert.Equal(original.Bar, deserialized.Bar);
    }

    [Fact]
    public void Can_Roundtrip_With_Them_Apples()
    {
        var original = new TwoObjects
        {
            One = new AppleRecord("Golden Delicious"),
            Two = new AppleRecord("Granny Smith")
        };

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<TwoObjects>(bytes);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.One);
        Assert.NotNull(deserialized.Two);
        Assert.Equal(original.One, deserialized.One);
        Assert.Equal(original.Two, deserialized.Two);
    }

    [Fact]
    public void Can_Roundtrip_WithListOfObject_Apple()
    {
        var original = new FooWithListOfObject
        {
            Items = new List<object> { new AppleRecord("Golden Delicious"), new AppleRecord("Granny Smith") },
            Bar = new FooRecord(Guid.NewGuid())
        };

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FooWithListOfObject>(bytes);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Items);
        Assert.Equal(original.Items.Count, deserialized.Items.Count);
        Assert.Equal(original.Items[0], deserialized.Items[0]);
        Assert.Equal(original.Items[1], deserialized.Items[1]);

        Assert.NotNull(deserialized.Bar);
        Assert.Equal(original.Bar, deserialized.Bar);
    }

    [Fact]
    public void Can_Roundtrip_WithListOfFruit_Fruit()
    {
        var original = new FooWithListOfFruit()
        {
            Items = new List<FruitRecord> { new FruitRecord("Banana"), new FruitRecord("Mango") },
            Bar = new FooRecord(Guid.NewGuid())
        };

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FooWithListOfFruit>(bytes);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Bar);
        Assert.Equal(original.Bar, deserialized.Bar);
    }

    [Fact]
    public void Can_Roundtrip_WithListOfFruit_Apple()
    {
        var original = new FooWithListOfFruit()
        {
            Items = new List<FruitRecord> { new AppleRecord("Golden Delicious"), new AppleRecord("Granny Smith") },
            Bar = new FooRecord(Guid.NewGuid())
        };

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FooWithListOfFruit>(bytes);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Bar);
        Assert.Equal(original.Bar, deserialized.Bar);
    }

    [Fact]
    public void Can_Roundtrip_WithListOfApple_Apple()
    {
        var original = new FooWithListOfApple()
        {
            Items = new List<AppleRecord> { new AppleRecord("Golden Delicious"), new AppleRecord("Granny Smith") },
            Bar = new FooRecord(Guid.NewGuid())
        };

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FooWithListOfApple>(bytes);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Bar);
        Assert.Equal(original.Bar, deserialized.Bar);
    }

    [Fact]
    public void Can_Roundtrip_Fruit_Apple()
    {
        FruitRecord original = new AppleRecord("Golden Delicious");

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FruitRecord>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Can_Roundtrip_Foo()
    {
        var original = new FooRecord(Guid.NewGuid());

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<FooRecord>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Can_Roundtrip_RecordWithParameterIdAttribute()
    {
        var original = new RecordWithParameterIdAttribute(42, "test");

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<RecordWithParameterIdAttribute>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
        Assert.Equal(42, deserialized.Value);
        Assert.Equal("test", deserialized.Name);
    }

    [Fact]
    public void Can_Roundtrip_ComplexRecordWithMixedIdAttributes()
    {
        var original = new ComplexRecordWithMixedIdAttributes(123, "primary", "extra");

        var bytes = _serializer.SerializeToArray(original);

        var deserialized = _serializer.Deserialize<ComplexRecordWithMixedIdAttributes>(bytes);

        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
        Assert.Equal(123, deserialized.Id);
        Assert.Equal("primary", deserialized.Name);
        Assert.Equal("extra", deserialized.Description);
    }

    // TODO: 此类型应造成构建错误, 因为“ bar” 是只使用 Init 的非自动属性, 但有 [ID(...) 属性 。
    // 适合进行诊断分析测试,但目前的实施情况
    // 源生成器不支持作为分析器执行。
    /*
    [GenerateSerializer]
    公开记录 RecordWithInitOnlyManualProperty
    {
        private string _bar;

        [日期0]
        公共字符串栏
        {
            获得 _bar;
            开进
            {
                _bar = 值;
                OnSetBar(_bar);
            }
        }

        RecordWithInitOnlyManualProperty(字符串栏)
        {
            _bar = bar;
            OnSetBar(_bar);
        }

        私人空 OnSetBar(字符串栏)
        {
            // 忽略忽略
            _ =巴;
        }
    }
    */
}

[GenerateSerializer]
public abstract record EmptyAbstractRecord
{
}

[GenerateSerializer]
public record DerivedFromEmptyAbstractRecord : EmptyAbstractRecord
{
    [Id(0)]
    public string Foo { get; init; }

    public DerivedFromEmptyAbstractRecord(string foo)
    {
        Foo = foo;
    }
}

[GenerateSerializer]
public abstract record NonEmptyAbstractRecord
{
    [Id(0)]
    public string Bar { get; init; }

    protected NonEmptyAbstractRecord()
    {
        Bar = "bar";
    }
}

[GenerateSerializer]
public record DerivedFromNonEmptyAbstractRecord : NonEmptyAbstractRecord
{
    [Id(0)]
    public string Foo { get; init; }

    public DerivedFromNonEmptyAbstractRecord(string foo) : base()
    {
        Foo = foo;
    }
}

[GenerateSerializer]
public record RecordHierarchyDerived([property: Id(1)] string Message) : RecordHierarchyMiddle
{
    public override string Type => "Foo";
}

[GenerateSerializer]
public abstract record RecordHierarchyMiddle : RecordHierarchyBase;

[GenerateSerializer]
public abstract record RecordHierarchyBase
{
    public abstract string Type { get; }
}

[GenerateSerializer]
public class FooWithListOfObject
{
    [Id(1)]
    public List<object>? Items { get; set; }

    [Id(2)]
    public FooRecord? Bar { get; set; }
}

[GenerateSerializer]
public class TwoObjects
{
    [Id(0)]
    public object? One { get; set; }

    [Id(1)]
    public object? Two { get; set; }
}

[GenerateSerializer]
public class FooWithListOfFruit
{
    [Id(1)]
    public List<FruitRecord>? Items { get; set; }

    [Id(2)]
    public FooRecord? Bar { get; set; }
}

[GenerateSerializer]
public class FooWithListOfApple
{
    [Id(1)]
    public List<AppleRecord>? Items { get; set; }

    [Id(2)]
    public FooRecord? Bar { get; set; }
}

[GenerateSerializer]
public record FruitRecord(string Name);

[GenerateSerializer]
public record AppleRecord(string Name) : FruitRecord(Name);

[GenerateSerializer]
public record FooRecord([property: Id(0)] Guid Id);

[GenerateSerializer]
public record RecordWithParameterIdAttribute([Id(10)] int Value, [Id(20)] string Name);

[GenerateSerializer]
public record ComplexRecordWithMixedIdAttributes([Id(30)] int Id, [Id(40)] string Name)
{
    [Id(50)]
    public string Description { get; init; } = string.Empty;

    public ComplexRecordWithMixedIdAttributes(int id, string name, string description) : this(id, name)
    {
        Description = description;
    }
}
