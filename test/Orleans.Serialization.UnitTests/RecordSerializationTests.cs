#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orleans.Serialization.UnitTests;

/// <summary>
/// Orleans 对 C# record 类型的支持测试。
/// 
/// record 是 Orleans 序列化的一等类型，全面支持：
/// - 主构造函数参数
/// - 仅初始化（init-only）属性
/// - record 继承层次结构
/// - 抽象 record 基类型
/// - 具有多层继承关系的复杂 record 结构
/// 
/// Orleans 通过以下方式高效处理 record：
/// - 生成理解 record 语义的优化序列化器
/// - 保留值相等性语义
/// - 同时支持位置式和具名式 record 语法
/// - 处理 record struct（值类型 record）
/// 
/// 此支持对于将 record 用于以下用途的现代 C# 应用至关重要：
/// - 不可变数据模型
/// - 使用值对象的领域驱动设计
/// - 函数式编程模式
/// - 清晰的 API 契约
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

    // TODO: 该类型应导致构建错误，因为“Bar”是仅初始化的非自动属性，却带有 [Id(...)] 特性。
    // 它适合作为诊断分析器测试，但当前
    // 源生成器实现不支持作为分析器执行。
    /*
    [GenerateSerializer]
    public record RecordWithInitOnlyManualProperty
    {
        private string _bar;

        [Id(0)]
        public string Bar
        {
            get => _bar;
            init
            {
                _bar = value;
                OnSetBar(_bar);
            }
        }

        public RecordWithInitOnlyManualProperty(string bar)
        {
            _bar = bar;
            OnSetBar(_bar);
        }

        private void OnSetBar(string bar)
        {
            // Ignore
            _ = bar;
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
