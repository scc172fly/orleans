using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGenerator.Diagnostics;
using Orleans.Serialization;

namespace Orleans.CodeGenerator.Tests;

/// <summary>
/// 测试 Orleans 源码生成器生成序列化及 RPC 代码的功能。
///
/// Orleans 源生成器使用 Roslyn 源生成器来：
/// - 为标记了 [GenerateSerializer] 的类型生成序列化器
/// - 为 Grain 接口生成代理类
/// - 为 Grain 方法生成可调用包装器
/// - 为 Orleans 运行时生成元数据
///
/// 测试的主要功能：
/// - 为各种类型模式生成序列化代码
/// - 支持不同的 C# 语言特性（记录、可空引用类型等）
/// - 为不同 Grain 键类型生成代理
/// - 正确处理泛型类型
/// - 对错误用法发出诊断
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("CodeGen")]
public class OrleansSourceGeneratorTests
{
    /// <summary>
    /// 测试为带字符串属性的简单类生成基础序列化器。
    /// 这是最常见的场景：属性标记了 [Id] 特性的 POCO。
    /// </summary>
    [Fact]
    public Task TestBasicClass() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class DemoData
{
    [Id(0)]
    public string Value { get; set; } = string.Empty;
}");

    /// <summary>
    /// 测试为具有私有只读字段的类生成序列化器。
    /// 验证生成器可以处理以下情况：
    /// - 私有字段（不仅是公共属性）
    /// - 必须通过构造函数设置的只读字段
    /// - 公开私有字段值的属性 getter
    /// </summary>
    [Fact]
    public Task TestBasicClassWithoutNamespace() => AssertSuccessfulSourceGeneration(
@"using Orleans;

[GenerateSerializer]
public class DemoData
{
    [Id(0)]
    public string Value { get; set; } = string.Empty;
}");

    [Fact]
    public Task TestBasicClassWithDifferentAccessModifiers() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class PublicDemoData
{
    [Id(0)]
    public string Value { get; set; } = string.Empty;
}

[GenerateSerializer]
internal class InternalDemoData
{
    [Id(0)]
    public string Value { get; set; } = string.Empty;
}");

    /// <summary>
    /// 测试为具有私有只读字段的类生成序列化器。
    /// 验证生成器可以处理以下情况：
    /// - 私有字段（不仅是公共属性）
    /// - 必须通过构造函数设置的只读字段
    /// - 公开私有字段值的属性 getter
    /// </summary>
    [Fact]
    public Task TestBasicClassWithAnnotatedFields() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class DemoDataWithFields
{
    [Id(0)]
    private readonly int _intValue;

    [Id(1)]
    private readonly string _stringValue;

    public DemoDataWithFields(int intValue, string stringValue)
    {
        _intValue = intValue;
        _stringValue = stringValue;
    }

    public int IntValue => _intValue;

    public string StringValue => _stringValue;
}");

    [Fact]
    public Task TestBasicClassWithInheritance() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public abstract class BaseData
{
    [Id(0)]
    public string BaseValue { get; set; } = string.Empty;

    protected BaseData(string value)
    {
        BaseValue = value;
    }
}

[GenerateSerializer]
public class DerivedData : BaseData
{
    [Id(1)]
    public string DerivedValue { get; set; } = string.Empty;

    public DerivedData(string baseValue, string derivedValue) : base(baseValue)
    {
        DerivedValue = derivedValue;
    }
}");

    /// <summary>
    /// 测试为值类型（结构体）生成序列化器。
    /// 结构体与类具有不同的语义（值类型与引用类型）
    /// 且生成器必须正确处理它们。
    /// </summary>
    [Fact]
    public Task TestBasicStruct() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public struct DemoData
{
    [Id(0)]
    public string Value { get; set; }
}");

    /// <summary>
    /// 测试为 C# 9 及更高版本的记录类型生成序列化器。
    /// 记录默认不可变，并使用位置参数，
    /// 因此需要对以下情况进行特殊处理：
    /// - 记录结构体与记录类
    /// - 位置参数上的属性特性
    /// - 仅可初始化的属性
    /// </summary>
    [Fact]
    public Task TestRecords() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public record struct DemoDataRecordStruct([property: Id(0)] string Value);

[GenerateSerializer]
public record class DemoDataRecordClass([property: Id(0)] string Value);

[GenerateSerializer]
public record DemoDataRecord([property: Id(0)] string Value);");

    /// <summary>
    /// 测试为主构造函数参数上具有 [Id] 特性的记录生成序列化器。
    /// </summary>
    [Fact]
    public Task TestRecordsWithParameterIdAttributes() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public record SimpleRecord([Id(10)] int Value, [Id(20)] string Name);

[GenerateSerializer]
public record RecordWithExtraProperty([Id(30)] int Id, [Id(40)] string Name)
{
    [Id(50)]
    public string Description { get; init; }
}

[GenerateSerializer]
public record struct RecordStructWithParameterId([Id(60)] string Value);");

    /// <summary>
    /// 测试为泛型类型生成序列化器。
    /// 泛型类型需要：
    /// - 为每个具体类型用法生成专用序列化器
    /// - 在序列化逻辑中处理类型参数
    /// - 支持嵌套泛型类型
    /// </summary>
    [Fact]
    public Task TestGenericClass() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class GenericData<T>
{
    [Id(0)]
    public T Value { get; set; }

    [Id(1)]
    public string Description { get; set; } = string.Empty;
}

// Also need a concrete usage to trigger generation for a specific type
[GenerateSerializer]
public class ConcreteUsage
{
    [Id(0)]
    public GenericData<int> IntData { get; set; }

    [Id(1)]
    public GenericData<string> StringData { get; set; }
}");

    /// <summary>
    /// 测试为具有可空引用类型的类生成序列化器。
    /// 验证对 C# 8 及更高版本可空引用类型的支持，包括：
    /// - 可空与不可空引用属性
    /// - 必需属性（C# 11 及更高版本）
    /// - 仅可初始化的 setter
    /// </summary>
    [Fact]
    public Task TestClassReferenceProperties() => AssertSuccessfulSourceGeneration(
@"#nullable enable
using Orleans;

namespace TestProject;

[GenerateSerializer]
public class DemoData
{
    [Id(0)]
    public string? NullableStringProp { get; set; }

    [Id(1)]
    public string StringProp { get; set; } = string.Empty;

    [Id(2)]
    public required string RequiredStringProp { get; set; }

    [Id(3)]
    public required string RequiredStringPropInitOnly { get; init; }
}");

    /// <summary>
    /// 测试为所有 .NET 基元类型生成序列化器。
    /// 确保生成器内置对以下情况的支持：
    /// - 数值类型（int、long、float、double、decimal 等）
    /// - Boolean、char、string
    /// - 日期与时间类型（DateTime、DateTimeOffset、TimeSpan）
    /// - GUID 和数组
    /// </summary>
    [Fact]
    public Task TestClassPrimitiveTypes() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System;

namespace TestProject;

[GenerateSerializer]
public class DemoData
{
    [Id(0)]
    public int IntProp { get; set; }

    [Id(1)]
    public double DoubleProp { get; set; }

    [Id(2)]
    public float FloatProp { get; set; }

    [Id(3)]
    public long LongProp { get; set; }

    [Id(4)]
    public bool BoolProp { get; set; }

    [Id(5)]
    public byte ByteProp { get; set; }

    [Id(6)]
    public short ShortProp { get; set; }

    [Id(7)]
    public char CharProp { get; set; }

    [Id(8)]
    public uint UIntProp { get; set; }

    [Id(9)]
    public ulong ULongProp { get; set; }

    [Id(10)]
    public ushort UShortProp { get; set; }

    [Id(11)]
    public sbyte SByteProp { get; set; }

    [Id(12)]
    public decimal DecimalProp { get; set; }

    [Id(13)]
    public DateTime DateTimeProp { get; set; }

    [Id(14)]
    public DateTimeOffset DateTimeOffsetProp { get; set; }

    [Id(15)]
    public TimeSpan TimeSpanProp { get; set; }

    [Id(16)]
    public Guid GuidProp { get; set; }

    [Id(17)]
    public int[] IntArrayProp { get; set; }
}");

    [Fact]
    public Task TestClassPrimitiveTypesUsingFullName() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class DemoData
{
    [Id(0)]
    public System.Int32 IntProp { get; set; }

    [Id(1)]
    public System.Double DoubleProp { get; set; }

    [Id(2)]
    public System.Single FloatProp { get; set; }

    [Id(3)]
    public System.Int64 LongProp { get; set; }

    [Id(4)]
    public System.Boolean BoolProp { get; set; }

    [Id(5)]
    public System.Byte ByteProp { get; set; }

    [Id(6)]
    public System.Int16 ShortProp { get; set; }

    [Id(7)]
    public System.Char CharProp { get; set; }

    [Id(8)]
    public System.UInt32 UIntProp { get; set; }

    [Id(9)]
    public System.UInt64 ULongProp { get; set; }

    [Id(10)]
    public System.UInt16 UShortProp { get; set; }

    [Id(11)]
    public System.SByte SByteProp { get; set; }

    [Id(12)]
    public System.Decimal DecimalProp { get; set; }

    [Id(13)]
    public System.DateTime DateTimeProp { get; set; }

    [Id(14)]
    public System.DateTimeOffset DateTimeOffsetProp { get; set; }

    [Id(15)]
    public System.TimeSpan TimeSpanProp { get; set; }

    [Id(16)]
    public System.Guid GuidProp { get; set; }

    [Id(17)]
    public System.Int32[] IntArrayProp { get; set; }
}");

    /// <summary>
    /// 测试为复杂对象图生成序列化器。
    /// 验证以下情况的处理：
    /// - 嵌套对象引用
    /// - 自定义类型的集合
    /// - 循环引用（对防止堆栈溢出至关重要）
    /// </summary>
    [Fact]
    public Task TestClassNestedTypes() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System.Collections.Generic;

namespace TestProject;

[GenerateSerializer]
public class DemoData
{
    [Id(0)]
    public NestedClass1 Nested1 { get; set; }

    [Id(1)]
    public List<NestedClass1> NestedList { get; set; }

    [Id(2)]
    public CyclicClass Cyclic { get; set; }
}

public class NestedClass1
{
    [Id(0)]
    public string Value { get; set; }

    [Id(1)]
    public NestedClass2 Nested2 { get; set; }
}

public class NestedClass2
{
    [Id(0)]
    public string Value { get; set; }

    [Id(1)]
    public int IntProp { get; set; }
}

public class CyclicClass
{
    [Id(0)]
    public CyclicClass Nested { get; set; }

    [Id(1)]
    public string Value { get; set; }
}");

    /// <summary>
    /// 测试用于类型名称别名的 [Alias] 特性。
    /// 别名允许在不破坏序列化兼容性的情况下重命名类型，
    /// 这对于版本管理及重构场景至关重要。
    /// </summary>
    [Fact]
    public Task TestAlias() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[Alias(""_custom_type_alias_"")]
public class MyTypeAliasClass
{
    [Id(0)]
    public string Name { get; set; }
}

[GenerateSerializer]
public struct MyTypeAliasStruct
{
    [Id(0)]
    public string Name { get; set; }
}
");

    /// <summary>
    /// 测试用于复杂类型别名的 [CompoundTypeAlias] 特性。
    /// 复合别名可以包含多个类型组成部分及版本，
    /// 从而支持类型迁移等高级版本管理场景。
    /// </summary>
    [Fact]
    public Task TestCompoundTypeAlias() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[Alias(""_custom_type_alias_"")]
public class MyTypeAliasClass
{
}

[GenerateSerializer]
public class MyCompoundTypeAliasBaseClass
{
    [Id(0)]
    public int BaseValue { get; set; }
}

[GenerateSerializer]
[CompoundTypeAlias(""xx_test_xx"", typeof(MyTypeAliasClass), typeof(int), ""1"")]
public class MyCompoundTypeAliasClass : MyCompoundTypeAliasBaseClass
{
    [Id(0)]
    public string Name { get; set; }

    [Id(1)]
    public int Value { get; set; }
}");

    [Fact]
    public Task TestClassWithParameterizedConstructor() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

public interface IMyService { }
public class MyService : IMyService { }

[GenerateSerializer]
public class MyServiceConsumer
{
    private readonly IMyService _service;
    private readonly int _value;

    // Constructor requiring parameters, which the generator should use for activation
    public MyServiceConsumer(IMyService service, int value)
    {
        _service = service;
        _value = value;
    }

    [Id(0)]
    public string Name { get; set; }
}

// Include a type that uses the above class to ensure it's processed
[GenerateSerializer]
public class RootType
{
    [Id(0)]
    public MyServiceConsumer Consumer { get; set; }
}");

    [Fact]
    public Task TestGenericClassWithConstructorParameters() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class GenericWithCtor<T>
{
    [Id(0)]
    private readonly T _value;
    [Id(1)]
    private readonly int _id;

    public GenericWithCtor(T value, int id)
    {
        _value = value;
        _id = id;
    }

    public T Value => _value;
    public int Id => _id;
}

[GenerateSerializer]
public class UsesGenericWithCtor
{
    [Id(0)]
    public GenericWithCtor<string> StringGen { get; set; }
}", snapshotName: nameof(TestGenericClassWithConstructorParameters));

    [Fact]
    public Task TestClassWithNoPublicConstructors() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class NoPublicCtor
{
    private NoPublicCtor() { }

    [Id(0)]
    public int Value { get; set; }
}");

    [Fact]
    public Task TestClassWithOptionalConstructorParameters() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class OptionalCtorParams
{
    [Id(0)]
    private readonly int _x;
    [Id(1)]
    private readonly string _y;

    public OptionalCtorParams(int x = 42, string y = ""default"")
    {
        _x = x;
        _y = y;
    }

    public int X => _x;
    public string Y => _y;
}");

    [Fact]
    public Task TestClassWithInterfaceConstructorParameter() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

public interface IMyInterface { }

[GenerateSerializer]
public class InterfaceCtorParam
{
    [Id(0)]
    private readonly IMyInterface _iface;

    public InterfaceCtorParam(IMyInterface iface)
    {
        _iface = iface;
    }

    public IMyInterface Iface => _iface;
}");

    [Fact]
    public Task TestClassesWithGeneratedActivatorConstructorAnnotation() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public class ClassWithGeneratedActivatorConstructor
{
    [Id(0)]
    public int Value { get; set; }

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [GeneratedActivatorConstructor]
    public ClassWithGeneratedActivatorConstructor()
    {
    }

    public ClassWithGeneratedActivatorConstructor(int value)
    {
        Value = value;
    }
}");

    [Fact]
    public Task TestClassWithGenerateMethodSerializersAnnotation() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using Orleans.Runtime;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IMyGrain : IGrainWithIntegerKey
{
    Task<string> SayHello(string name);
}");

    [Fact]
    public async Task ExplicitMethodAlias_TakesPrecedenceOverAnotherOverloadsGeneratedAlias()
    {
        var compilation = await CreateCompilation(
@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IBasicGrain : IGrainWithIntegerKey
{
    [Alias(""SayHello"")]
    Task<string> SayHello(string name);

    [Alias(""6B0E24A1"")]
    Task<string> SayHello(string name, CancellationToken cancellationToken);
}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        var generatedSource = ConcatenateGeneratedSources(result);
        Assert.Contains("\"SayHello\"", generatedSource, StringComparison.Ordinal);
        Assert.Contains("\"6B0E24A1\"", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityOverload_UsesLegacyInvokerIdentityForOutboundCalls()
    {
        var baseline = await CreateCompilation(
@"using Orleans;
using System.Threading.Tasks;

public interface IBasicGrain : IGrainWithIntegerKey
{
    Task Ping(string value);
}",
            "Baseline");
        var legacyMethod = Assert.Single(baseline.GetTypeByMetadataName("IBasicGrain")!.GetMembers().OfType<IMethodSymbol>());
        var legacyMethodId = GeneratedCodeUtilities.CreateHashedMethodId(legacyMethod);
        var compilation = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IBasicGrain : IGrainWithIntegerKey
{{
    [Alias(""Ping"")]
    Task Ping(string value);

    [Alias(""{legacyMethodId}"")]
    Task Ping(string value, CancellationToken cancellationToken) => Ping(value);
}}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        var generatedSource = ConcatenateGeneratedSources(result);
        var generatedRoot = CSharpSyntaxTree.ParseText(
                generatedSource,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var proxyMethod = Assert.Single(
            generatedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(static declaration => declaration.Identifier.ValueText == "Proxy_IBasicGrain")
                .Members.OfType<MethodDeclarationSyntax>(),
            static method => method.Identifier.ValueText == "Ping" && method.ParameterList.Parameters.Count == 1);
        Assert.Contains(
            "global::System.Threading.CancellationToken.None",
            proxyMethod.Body!.ToString(),
            StringComparison.Ordinal);

        var cancellationMethod = Assert.Single(
            compilation.GetTypeByMetadataName("IBasicGrain")!.GetMembers("Ping").OfType<IMethodSymbol>(),
            static method => method.Parameters.Length == 2);
        var cancellationGeneratedId = GeneratedCodeUtilities.CreateHashedMethodId(cancellationMethod);
        var cancellationInvoker = generatedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText.StartsWith("Invokable_", StringComparison.Ordinal)
                && declaration.Identifier.ValueText.EndsWith(cancellationGeneratedId, StringComparison.Ordinal));
        var alias = Assert.Single(
            cancellationInvoker.AttributeLists.SelectMany(static list => list.Attributes),
            static attribute => attribute.Name.ToString().EndsWith("CompoundTypeAliasAttribute", StringComparison.Ordinal));
        Assert.Contains(legacyMethodId, alias.ToString(), StringComparison.Ordinal);
        var generatedAliasRegistration = Assert.Single(
            generatedSource.Split(Environment.NewLine),
            line => line.Contains($"Add(\"{cancellationGeneratedId}\", typeof(", StringComparison.Ordinal)
                && line.Contains(cancellationInvoker.Identifier.ValueText, StringComparison.Ordinal));
        Assert.Contains(cancellationGeneratedId, generatedAliasRegistration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericCompatibilityOverload_ForwardsToCancellationOverload()
    {
        var baseline = await CreateCompilation(
@"using Orleans;
using System.Threading.Tasks;

public interface IBasicGrain : IGrainWithIntegerKey
{
    Task<T> RoundTrip<T>(T value);
}",
            "Baseline");
        var legacyMethod = Assert.Single(baseline.GetTypeByMetadataName("IBasicGrain")!.GetMembers().OfType<IMethodSymbol>());
        var legacyMethodId = GeneratedCodeUtilities.CreateHashedMethodId(legacyMethod);
        var compilation = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IBasicGrain : IGrainWithIntegerKey
{{
    [Alias(""RoundTrip"")]
    Task<T> RoundTrip<T>(T value);

    [Alias(""{legacyMethodId}"")]
    Task<T> RoundTrip<T>(T value, CancellationToken cancellationToken) => RoundTrip(value);
}}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        var generatedRoot = CSharpSyntaxTree.ParseText(
                ConcatenateGeneratedSources(result),
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var proxyMethod = Assert.Single(
            generatedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(static declaration => declaration.Identifier.ValueText == "Proxy_IBasicGrain")
                .Members.OfType<MethodDeclarationSyntax>(),
            static method => method.Identifier.ValueText == "RoundTrip" && method.ParameterList.Parameters.Count == 1);
        var proxyBody = proxyMethod.Body!.ToString();
        Assert.Contains("RoundTrip<T>", proxyBody, StringComparison.Ordinal);
        Assert.Contains("global::System.Threading.CancellationToken.None", proxyBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericCompatibilityOverload_WithStricterConstraints_DoesNotForward()
    {
        var baseline = await CreateCompilation(
@"using Orleans;
using System.Threading.Tasks;

public interface IBasicGrain : IGrainWithIntegerKey
{
    Task<T> RoundTrip<T>(T value);
}",
            "Baseline");
        var legacyMethod = Assert.Single(baseline.GetTypeByMetadataName("IBasicGrain")!.GetMembers().OfType<IMethodSymbol>());
        var legacyMethodId = GeneratedCodeUtilities.CreateHashedMethodId(legacyMethod);
        var compilation = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IBasicGrain : IGrainWithIntegerKey
{{
    [Alias(""RoundTrip"")]
    Task<T> RoundTrip<T>(T value);

    [Alias(""{legacyMethodId}"")]
    Task<T> RoundTrip<T>(T value, CancellationToken cancellationToken) where T : struct;
}}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        var generatedRoot = CSharpSyntaxTree.ParseText(
                ConcatenateGeneratedSources(result),
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var proxyMethod = Assert.Single(
            generatedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single(static declaration => declaration.Identifier.ValueText == "Proxy_IBasicGrain")
                .Members.OfType<MethodDeclarationSyntax>(),
            static method => method.Identifier.ValueText == "RoundTrip" && method.ParameterList.Parameters.Count == 1);
        Assert.DoesNotContain(
            "global::System.Threading.CancellationToken.None",
            proxyMethod.Body!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitMethodId_TakesPrecedenceOverAnotherOverloadsGeneratedAlias()
    {
        var candidateMethods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 128).Select(static index => $"    ValueTask Method{index}();"));
        var baseline = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading.Tasks;

public interface IBasicGrain : IGrainWithIntegerKey
{{
{candidateMethods}
}}",
            "Baseline");
        var candidate = baseline.GetTypeByMetadataName("IBasicGrain")!.GetMembers()
            .OfType<IMethodSymbol>()
            .Select(static method => (Method: method, Id: GeneratedCodeUtilities.CreateHashedMethodId(method)))
            .First(static entry => entry.Id.Any(static character => character is >= 'A' and <= 'F'));
        var explicitMethodId = Convert.ToUInt32(candidate.Id, 16).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var methodName = candidate.Method.Name;
        var compilation = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IBasicGrain : IGrainWithIntegerKey
{{
    [Alias(""{methodName}"")]
    ValueTask {methodName}();

    [Id(0x{candidate.Id}u)]
    ValueTask {methodName}(CancellationToken cancellationToken);
}}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        var interfaceSymbol = compilation.GetTypeByMetadataName("IBasicGrain")!;
        var cancellationMethod = Assert.Single(
            interfaceSymbol.GetMembers(methodName).OfType<IMethodSymbol>(),
            static method => method.Parameters.Length == 1);
        var cancellationGeneratedId = GeneratedCodeUtilities.CreateHashedMethodId(cancellationMethod);
        var generatedSource = ConcatenateGeneratedSources(result);
        var generatedRoot = CSharpSyntaxTree.ParseText(
                generatedSource,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var cancellationInvoker = generatedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText.StartsWith("Invokable_", StringComparison.Ordinal)
                && declaration.Identifier.ValueText.EndsWith(cancellationGeneratedId, StringComparison.Ordinal));
        var outboundAlias = Assert.Single(
            cancellationInvoker.AttributeLists.SelectMany(static list => list.Attributes),
            static attribute => attribute.Name.ToString().EndsWith("CompoundTypeAliasAttribute", StringComparison.Ordinal));
        Assert.Contains(candidate.Id, outboundAlias.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(explicitMethodId, outboundAlias.ToString(), StringComparison.Ordinal);
        var registration = Assert.Single(
            generatedSource.Split(Environment.NewLine),
            line => line.Contains($"Add(\"{candidate.Id}\", typeof(", StringComparison.Ordinal));
        Assert.Contains(cancellationGeneratedId, registration, StringComparison.Ordinal);
        var explicitIdRegistration = Assert.Single(
            generatedSource.Split(Environment.NewLine),
            line => line.Contains($"Add(\"{explicitMethodId}\", typeof(", StringComparison.Ordinal));
        Assert.Contains(cancellationGeneratedId, explicitIdRegistration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericCompatibilityOverload_WithReorderedConstraints_Forwards()
    {
        var baseline = await CreateCompilation(
@"using Orleans;
using System.Threading.Tasks;

public interface IBasicGrain : IGrainWithIntegerKey
{
    Task<T> RoundTrip<T>(T value) where T : IFoo, IBar;
}

public interface IFoo { }
public interface IBar { }",
            "Baseline");
        var legacyMethod = Assert.Single(baseline.GetTypeByMetadataName("IBasicGrain")!.GetMembers().OfType<IMethodSymbol>());
        var legacyMethodId = GeneratedCodeUtilities.CreateHashedMethodId(legacyMethod);
        var compilation = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IBasicGrain : IGrainWithIntegerKey
{{
    [Alias(""RoundTrip"")]
    Task<T> RoundTrip<T>(T value) where T : IFoo, IBar;

    [Alias(""{legacyMethodId}"")]
    Task<T> RoundTrip<T>(T value, CancellationToken cancellationToken) where T : IBar, IFoo;
}}

public interface IFoo {{ }}
public interface IBar {{ }}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        var generatedSource = ConcatenateGeneratedSources(result);
        Assert.Contains(
            "global::System.Threading.CancellationToken.None",
            generatedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitExtensionAlias_DoesNotSuppressAnotherDeclaringInterfacesGeneratedAlias()
    {
        var baseline = await CreateCompilation(
@"using Orleans;
using Orleans.Runtime;
using System.Threading.Tasks;

public interface IBaseExtensionA : IGrainExtension
{
    ValueTask FlushBuffers();
}",
            "Baseline");
        var method = Assert.Single(baseline.GetTypeByMetadataName("IBaseExtensionA")!.GetMembers().OfType<IMethodSymbol>());
        var generatedMethodId = GeneratedCodeUtilities.CreateHashedMethodId(method);
        var compilation = await CreateCompilation(
$@"using Orleans;
using Orleans.Runtime;
using System.Threading;
using System.Threading.Tasks;

public interface IBaseExtensionA : IGrainExtension
{{
    [Alias(""FlushBuffers"")]
    ValueTask FlushBuffers();
}}

public interface IBaseExtensionB : IGrainExtension
{{
    [Alias(""{generatedMethodId}"")]
    ValueTask FlushBuffers(CancellationToken cancellationToken);
}}

[GenerateMethodSerializers(typeof(GrainReference))]
public interface IDerivedExtension : IBaseExtensionA, IBaseExtensionB
{{
}}",
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);
        var derivedInterface = compilation.GetTypeByMetadataName("IDerivedExtension")!;
        var derivedMethods = derivedInterface.AllInterfaces
            .SelectMany(static interfaceType => interfaceType.GetMembers("FlushBuffers"))
            .OfType<IMethodSymbol>()
            .ToArray();
        var legacyGeneratedId = GeneratedCodeUtilities.CreateHashedMethodId(
            Assert.Single(derivedMethods, static method => method.Parameters.Length == 0));
        var cancellationGeneratedId = GeneratedCodeUtilities.CreateHashedMethodId(
            Assert.Single(derivedMethods, static method => method.Parameters.Length == 1));
        var generatedSource = ConcatenateGeneratedSources(result);
        var registrations = generatedSource.Split(Environment.NewLine)
            .Where(line => line.Contains($"Add(\"{generatedMethodId}\", typeof(", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(
            registrations,
            line => line.Contains(
                $"Invokable_IBaseExtensionA_GrainReference_Ext_{legacyGeneratedId}",
                StringComparison.Ordinal));
        Assert.Contains(
            registrations,
            line => line.Contains(
                $"Invokable_IBaseExtensionB_GrainReference_Ext_{cancellationGeneratedId}",
                StringComparison.Ordinal));
    }

    [Fact]
    public Task TestClassWithGenerateSerializerAnnotation() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer]
public enum MyCustomEnum
{
    None,
    One,
    Two,
    Three
}

[GenerateSerializer(GenerateFieldIds = GenerateFieldIds.PublicProperties), Immutable]
public class ClassWithImplicitFieldIds
{
    public string StringValue { get; set; } = string.Empty;
    public MyCustomEnum EnumValue { get; set; }
}");

    /// <summary>
    /// 测试为基础 Grain 接口生成代理。
    /// 验证生成器会创建以下内容：
    /// - 实现 Grain 接口的代理类
    /// - 用于 RPC 调用的方法调用器
    /// - 与 Orleans 运行时正确集成
    /// </summary>
    [Fact]
    public Task TestBasicGrain() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System.Threading.Tasks;

namespace TestProject;

public interface IBasicGrain : IGrainWithIntegerKey
{
    Task<string> SayHello(string name);
}

[GenerateSerializer]
public class BasicGrain : Grain, IBasicGrain
{
    public Task<string> SayHello(string name)
    {
        return Task.FromResult($""Hello, {name}!"");
    }
}");

    /// <summary>
    /// 测试为具有不同键类型的 Grain 生成代理。
    /// Orleans 支持多种 Grain 键类型：
    /// - 整数键
    /// - GUID 键
    /// - 字符串键
    /// - 复合键（主键加扩展部分）
    /// 每种键类型均需要不同的代理生成逻辑。
    /// </summary>
    [Fact]
    public Task TestGrainWithDifferentKeyTypes() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System;
using System.Threading.Tasks;

namespace TestProject;

public interface IMyGrainWithGuidKey : IGrainWithGuidKey
{
    Task<Guid> GetGuidValue();
}

[GenerateSerializer]
public class GrainWithGuidKey : Grain, IMyGrainWithGuidKey
{
    public Task<Guid> GetGuidValue() => Task.FromResult(this.GetPrimaryKey());
}

public interface IMyGrainWithStringKey : IGrainWithStringKey
{
    Task<string> GetStringKey();
}

[GenerateSerializer]
public class GrainWithStringKey : Grain, IMyGrainWithStringKey
{
    public Task<string> GetStringKey() => Task.FromResult(this.GetPrimaryKeyString());
}

public interface IMyGrainWithGuidCompoundKey : IGrainWithGuidCompoundKey
{
    Task<Tuple<Guid, string>> GetGuidAndStringKey();
}

[GenerateSerializer]
public class GrainWithGuidCompoundKey : Grain, IMyGrainWithGuidCompoundKey
{
    public Task<Tuple<Guid, string>> GetGuidAndStringKey()
    {
        Guid primaryKey = this.GetPrimaryKey(out var keyExtension);
        return Task.FromResult(Tuple.Create(primaryKey, keyExtension));
    }
}

public interface IMyGrainWithIntegerCompoundKey : IGrainWithIntegerCompoundKey
{
    Task<Tuple<long, string>> GetIntegerAndStringKey();
}

[GenerateSerializer]
public class GrainWithIntegerCompoundKey : Grain, IMyGrainWithIntegerCompoundKey
{
    public Task<Tuple<long, string>> GetIntegerAndStringKey()
    {
        long primaryKey = this.GetPrimaryKeyLong(out var keyExtension);
        return Task.FromResult(Tuple.Create(primaryKey, keyExtension));
    }
}");

    /// <summary>
    /// 测试具有复杂方法签名的 Grain 代理生成。
    /// 验证生成器能否正确处理：
    /// - 多个参数
    /// - 自定义类型的参数与返回值
    /// - 异步 Task 返回类型
    /// </summary>
    [Fact]
    public Task TestGrainComplexGrain() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System.Threading;
using System.Threading.Tasks;

namespace TestProject;

[GenerateSerializer]
public class ComplexData
{
    [Id(0)]
    public int IntValue { get; set; }

    [Id(1)]
    public string StringValue { get; set; }
}

public interface IComplexGrain : IGrainWithIntegerKey
{
    Task<ComplexData> ProcessData(int inputInt, string inputString, ComplexData data, CancellationToken ctx);
}

[GenerateSerializer]
public class ComplexGrain : Grain, IComplexGrain
{
    public Task<ComplexData> ProcessData(int inputInt, string inputString, ComplexData data, CancellationToken ctx)
    {
        var result = new ComplexData
        {
            IntValue = inputInt * 2 + data.IntValue,
            StringValue = $""Processed: {inputString}"" + data.StringValue
        };
        return Task.FromResult(result);
    }
}");

    [Fact]
    public Task TestGrainWithMultipleInterfaces() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System.Threading.Tasks;

namespace TestProject;

public interface IGrainA : IGrainWithIntegerKey
{
    Task<string> MethodA(string input);
}

public interface IGrainB : IGrainWithIntegerKey
{
    Task<string> MethodB(string input);
}

public class RealGrain : Grain, IGrainA, IGrainB
{
    public Task<string> MethodA(string input)
    {
        return Task.FromResult($""GrainA: {input}!"");
    }

    public Task<string> MethodB(string input)
    {
        return Task.FromResult($""GrainB: {input}!"");
    }
}");

    [Fact]
    public Task TestGrainMethodAnnotatedWithResponseTimeout() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using System.Threading.Tasks;

namespace TestProject;

public interface IResponseTimeoutGrain : IGrainWithIntegerKey
{
    [ResponseTimeout(""00:00:10"")]
    Task<string> LongRunningMethod(string input);
}

public class ResponseTimeoutGrain : Grain, IResponseTimeoutGrain
{
    public Task<string> LongRunningMethod(string input)
    {
        // Simulate a long-running operation
        return Task.FromResult($""ResponseTimeoutGrain: {input}!"");
    }
}");

    [Fact]
    public Task TestGrainMethodAnnotatedWithInvokableBaseType() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using Orleans.Runtime;

using System;
using System.Threading.Tasks;

namespace TestProject;

[InvokableCustomInitializer(nameof(LoggerRequest.SetLoggingOptions))]
[InvokableBaseType(typeof(GrainReference), typeof(Task), typeof(LoggerRequest))]
[AttributeUsage(AttributeTargets.Method)]
public sealed class LoggingRcpAttribute : Attribute
{
    public LoggingRcpAttribute(string options)
    {
    }
}

public abstract class LoggerRequest : RequestBase
{
    public void SetLoggingOptions(string options)
    {
    }
}

public interface IHelloGrain : IGrainWithIntegerKey
{
    [LoggingRcp(""Hello"")]
    Task<string> SayHello(string greeting);
}

[GenerateSerializer]
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting)
    {
        return Task.FromResult($""Hello, {greeting}!"");
    }
}");

    [Fact]
    public Task TestWithUseActivatorAnnotation() => AssertSuccessfulSourceGeneration(
@"using Orleans;
using Orleans.Serialization.Activators;

namespace TestProject;

[UseActivator]
public class DemoClass
{
}

[RegisterActivator]
internal sealed class DemoClassActivator : IActivator<DemoClass>
{
    public DemoClass Create() => new DemoClass();
}");

    [Fact]
    public Task TestWithSerializerTransparentAnnotation() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[SerializerTransparent]
public abstract class DemoTransparentClass
{
}");

    [Fact]
    public Task TestWithSuppressReferenceTrackingAttribute() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer, SuppressReferenceTracking]
public class DemoClass
{
    [Id(0)]
    public string Value { get; set; } = string.Empty;
}");

    [Fact]
    public Task TestWithOmitDefaultMemberValuesAnnotation() => AssertSuccessfulSourceGeneration(
@"using Orleans;

namespace TestProject;

[GenerateSerializer, OmitDefaultMemberValues]
public class DemoClass
{
    [Id(0)]
    public string Value { get; set; }
}");

    /// <summary>
    /// 测试多个 Grain 接口共享公共基础接口时，可调用类型能否正确去重。
    /// 基础方法 <c>DoWork</c> 由两个接口
    /// <c>IGrainA</c> 与 <c>IGrainB</c> 继承，但生成器对于每个声明接口
    /// 应仅为该方法生成一个可调用类，而不是为每个
    /// 派生接口重复生成。
    /// 当前活动的代理流水线通过代理生成状态中的
    /// <c>_invokableMethodDescriptions</c> 字典处理此行为。
    /// </summary>
    [Fact]
    public async Task SharedBaseInterfaceMethodProducesDeduplicatedInvokable()
    {
        var code = """
            using Orleans;
            using System.Threading.Tasks;

            namespace TestProject;

            public interface IBaseGrain : IGrainWithGuidKey
            {
                Task DoWork();
            }

            public interface IGrainA : IBaseGrain
            {
                Task DoExtraA();
            }

            public interface IGrainB : IBaseGrain
            {
                Task DoExtraB();
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));

        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        var generatedSource = ConcatenateGeneratedSources(result);

        // 基础方法 DoWork 声明于 IBaseGrain。无论多少接口
        // 继承 IBaseGrain，都应仅为 IBaseGrain 上的 DoWork 生成一个可调用类型。
        var invokableDoWorkCount = System.Text.RegularExpressions.Regex.Matches(
            generatedSource, @"sealed class Invokable_IBaseGrain_GrainReference_\w+").Count;
        Assert.Equal(1, invokableDoWorkCount);

        // 每个派生接口都应为其独有的方法生成自己的可调用类型。
        Assert.Contains("Invokable_IGrainA_GrainReference_", generatedSource);
        Assert.Contains("Invokable_IGrainB_GrainReference_", generatedSource);
    }

    [Fact]
    public async Task DerivedInterfaceInheritingGenerateMethodSerializersProducesProxy()
    {
        var code = """
            using Orleans;
            using Orleans.Runtime;
            using System.Threading.Tasks;

            namespace TestProject;

            [GenerateMethodSerializers(typeof(GrainReference))]
            public interface IBaseGrain : IGrainWithIntegerKey
            {
                Task Ping();
            }

            public interface IDerivedGrain : IBaseGrain
            {
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));

        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        var generatedSource = ConcatenateGeneratedSources(result);
        Assert.Contains("Proxy_IDerivedGrain", generatedSource);
        Assert.Contains("Invokable_IBaseGrain_GrainReference_", generatedSource);
    }

    /// <summary>
    /// 测试编译级引用程序集提取路径能否正确处理
    /// 通过 [GenerateCodeForDeclaringAssembly] 发现的类型。由于这些类型位于其他程序集，
    /// ForAttributeWithMetadataName 无法找到它们。
    /// </summary>
    [Fact]
    public async Task GeneratesSerializersForReferencedAssemblyTypesViaGenerateCodeForDeclaringAssembly()
    {
        var libraryCode = """
            using Orleans;

            namespace LibraryProject;

            [GenerateSerializer]
            public class LibraryDto
            {
                [Id(0)]
                public string Name { get; set; } = string.Empty;

                [Id(1)]
                public int Value { get; set; }
            }
        """;

        var libraryCompilation = await CreateCompilation(libraryCode, "LibraryProject");
        Assert.Empty(
            libraryCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        // 在库上运行生成器，使其输出程序集包含生成的元数据。
        var libraryGeneratorResult = RunSourceGenerator(libraryCompilation);
        Assert.Empty(libraryGeneratorResult.Diagnostics);

        // 构建使用 [GenerateCodeForDeclaringAssembly] 引用库的使用方
        var consumerCode = """
            using Orleans;

            [assembly: GenerateCodeForDeclaringAssembly(typeof(LibraryProject.LibraryDto))]
        """;

        var consumerCompilation = await CreateCompilation(consumerCode, "ConsumerProject");

        // 将库添加为元数据引用，使使用方能够访问其类型
        consumerCompilation = consumerCompilation.AddReferences(libraryCompilation.ToMetadataReference());
        Assert.Empty(
            consumerCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var consumerResult = RunSourceGenerator(consumerCompilation);
        Assert.Empty(consumerResult.Diagnostics);
        Assert.NotEmpty(consumerResult.GeneratedSources);

        var generatedSource = ConcatenateGeneratedSources(consumerResult);

        // 生成器应为引用程序集中的 LibraryDto 生成序列化器（编解码器）。
        Assert.Contains("LibraryDto", generatedSource);
        Assert.Contains("Codec", generatedSource);
    }

    [Fact]
    public async Task GeneratesSerializersForReferencedNestedAndGenericAssemblyTypesViaGenerateCodeForDeclaringAssembly()
    {
        var libraryCode = """
            using Orleans;

            namespace LibraryProject;

            [GenerateSerializer]
            public sealed class GenericDto<T>
            {
                [Id(0)]
                public T Value { get; set; } = default!;
            }

            public sealed class Container
            {
                [GenerateSerializer]
                public sealed class NestedDto
                {
                    [Id(0)]
                    public int Value { get; set; }
                }

                [GenerateSerializer]
                public sealed class NestedGenericDto<T>
                {
                    [Id(0)]
                    public T Value { get; set; } = default!;
                }
            }
        """;

        var libraryCompilation = await CreateCompilation(libraryCode, "LibraryProject");
        Assert.Empty(
            libraryCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var consumerCode = """
            using Orleans;

            [assembly: GenerateCodeForDeclaringAssembly(typeof(LibraryProject.Container.NestedGenericDto<>))]
        """;

        var consumerCompilation = (await CreateCompilation(consumerCode, "ConsumerProject"))
            .AddReferences(libraryCompilation.ToMetadataReference());
        Assert.Empty(
            consumerCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var consumerResult = RunSourceGenerator(consumerCompilation);
        Assert.Empty(consumerResult.Diagnostics);

        Assert.Contains(
            consumerResult.GeneratedSources,
            source => source.HintName.Contains("LibraryProject.GenericDto", StringComparison.Ordinal));
        Assert.Contains(
            consumerResult.GeneratedSources,
            source => source.HintName.Contains("LibraryProject.Container.NestedDto", StringComparison.Ordinal));
        Assert.Contains(
            consumerResult.GeneratedSources,
            source => source.HintName.Contains("LibraryProject.Container.NestedGenericDto", StringComparison.Ordinal));

        var generatedSource = ConcatenateGeneratedSources(consumerResult);
        Assert.Contains("global::LibraryProject.GenericDto", generatedSource);
        Assert.Contains("global::LibraryProject.Container.NestedDto", generatedSource);
        Assert.Contains("global::LibraryProject.Container.NestedGenericDto", generatedSource);
    }

    [Fact]
    public async Task ReferencedSerializerResolutionUsesAssemblyMetadataIdentityWhenConsumerShadowsFullName()
    {
        var libraryCode = """
            using Orleans;

            namespace LibraryProject
            {
                public sealed class Marker
                {
                }
            }

            namespace Shadowed
            {
                [GenerateSerializer]
                public sealed class DuplicateDto
                {
                    [Id(0)]
                    public int LibraryValue { get; set; }
                }
            }
        """;

        var libraryCompilation = await CreateCompilation(libraryCode, "LibraryProject");
        Assert.Empty(
            libraryCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var consumerCode = """
            using Orleans;

            [assembly: GenerateCodeForDeclaringAssembly(typeof(LibraryProject.Marker))]

            namespace Shadowed;

            public sealed class DuplicateDto
            {
                public string ConsumerValue { get; set; } = string.Empty;
            }
        """;

        var consumerCompilation = (await CreateCompilation(consumerCode, "ConsumerProject"))
            .AddReferences(libraryCompilation.ToMetadataReference());
        Assert.Empty(
            consumerCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var consumerResult = RunSourceGenerator(consumerCompilation);
        Assert.Empty(consumerResult.Diagnostics);

        var generatedSource = ConcatenateGeneratedSources(consumerResult);
        Assert.Contains("LibraryValue", generatedSource);
        Assert.DoesNotContain("ConsumerValue", generatedSource);
    }

    [Fact]
    public async Task GeneratesProxiesForGenericAndNestedInterfaces()
    {
        var code = """
            using Orleans;
            using Orleans.Runtime;
            using System.Threading.Tasks;

            namespace TestProject;

            [GenerateMethodSerializers(typeof(GrainReference))]
            public interface IGenericGrain<T> : IGrainWithIntegerKey
            {
                Task<T> Echo(T value);
            }

            public sealed class Container
            {
                [GenerateMethodSerializers(typeof(GrainReference))]
                public interface INestedGrain : IGrainWithIntegerKey
                {
                    Task Ping();
                }

                [GenerateMethodSerializers(typeof(GrainReference))]
                public interface INestedGenericGrain<T> : IGrainWithIntegerKey
                {
                    Task<T> Echo(T value);
                }
            }
        """;

        var compilation = await CreateCompilation(code, "TestProject");
        Assert.Empty(
            compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        Assert.Contains(
            result.GeneratedSources,
            source => source.HintName.Contains("IGenericGrain", StringComparison.Ordinal));
        Assert.Contains(
            result.GeneratedSources,
            source => source.HintName.Contains("Container.INestedGrain", StringComparison.Ordinal));
        Assert.Contains(
            result.GeneratedSources,
            source => source.HintName.Contains("Container.INestedGenericGrain", StringComparison.Ordinal));

        var generatedSource = ConcatenateGeneratedSources(result);
        Assert.Contains("Proxy_IGenericGrain", generatedSource);
        Assert.Contains("Proxy_INestedGrain", generatedSource);
        Assert.Contains("Proxy_INestedGenericGrain", generatedSource);
    }

    /// <summary>
    /// 测试在引用程序集中使用 [GenerateSerializer] 时，生成器是否发出警告。
    /// 引用程序集仅包含元数据，不包含实现，因此在其中生成序列化器
    /// 是错误的。此测试确保开发者能够收到针对该错误的正确诊断。
    /// </summary>
    [Fact]
    public async Task EmitsWarningForGenerateSerializerInReferenceAssembly()
    {
        var code = """
            using Orleans;

            namespace TestProject;

            [GenerateSerializer]
            public class RefAsmType
            {
                [Id(0)]
                public string Value { get; set; } = string.Empty;
            }
        """;

        // ReferenceAssemblyAttribute 将程序集标记为引用程序集。
        // 如果在这样的程序集中使用 [GenerateSerializer]，会触发 Orleans 代码生成器发出诊断的逻辑。
        var compilation = await CreateCompilation(code, "TestProject");
        var referenceAssemblyAttribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName("System.Runtime.CompilerServices.ReferenceAssemblyAttribute"));
        var assemblyAttr = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(referenceAssemblyAttribute))
            .WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.AssemblyKeyword)));
        var root = (CSharpSyntaxNode)compilation.SyntaxTrees[0].GetRoot(TestContext.Current.CancellationToken);
        var newRoot = ((CompilationUnitSyntax)root).AddAttributeLists(assemblyAttr);
        var newTree = compilation.SyntaxTrees[0].WithRootAndOptions(newRoot, compilation.SyntaxTrees[0].Options);

        // 仅保留带 ReferenceAssemblyAttribute 的 syntaxTree
        compilation = compilation.RemoveSyntaxTrees(compilation.SyntaxTrees[0]).AddSyntaxTrees(newTree);

        var result = RunSourceGenerator(compilation);
        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticRuleId.ReferenceAssemblyWithGenerateSerializer);
    }

    [Fact]
    public async Task EmitsInvalidFieldIdDiagnosticForImplicitPublicProperties()
    {
        const string code = """
            using Orleans;

            namespace TestProject;

            [GenerateSerializer]
            public class AutoDto
            {
                public string Value { get; set; } = string.Empty;
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        Assert.Empty(
            compilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error));

        var result = RunSourceGenerator(compilation);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == CanNotGenerateImplicitFieldIdsDiagnostic.DiagnosticId);

        Assert.Equal(DiagnosticRuleId.CanNotGenerateImplicitFieldIds, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("AutoDto", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovedCustomAttributeBuildPropertiesAreIgnored()
    {
        var code = """
            using System;
            using Orleans;

            namespace TestProject;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public sealed class CustomGenerateSerializerAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class CustomImmutableAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public sealed class CustomAliasAttribute : Attribute
            {
                public CustomAliasAttribute(string value)
                {
                }
            }

            [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
            public sealed class CustomIdAttribute : Attribute
            {
                public CustomIdAttribute(uint id)
                {
                }
            }

            [CustomGenerateSerializer, CustomAlias("custom")]
            public class DemoData
            {
                [CustomId(0), CustomImmutable]
                public string Value { get; set; } = string.Empty;
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var baselineResult = RunSourceGenerator(compilation);
        var configuredResult = RunSourceGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.orleans_immutableattributes"] = "TestProject.CustomImmutableAttribute",
                ["build_property.orleans_idattributes"] = "TestProject.CustomIdAttribute",
                ["build_property.orleans_aliasattributes"] = "TestProject.CustomAliasAttribute",
                ["build_property.orleans_generateserializerattributes"] = "TestProject.CustomGenerateSerializerAttribute",
            });

        Assert.NotEmpty(baselineResult.GeneratedSources);
        Assert.NotEmpty(configuredResult.GeneratedSources);
        Assert.Equal(
            baselineResult.GeneratedSources.Select(source => source.HintName).OrderBy(name => name),
            configuredResult.GeneratedSources.Select(source => source.HintName).OrderBy(name => name));
        Assert.Equal(
            ConcatenateGeneratedSources(baselineResult),
            ConcatenateGeneratedSources(configuredResult));
        Assert.Equal(
            baselineResult.Diagnostics.Select(d => d.Id).OrderBy(id => id),
            configuredResult.Diagnostics.Select(d => d.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task GlobalGenerateFieldIdsOption_AllowsImplicitPublicProperties()
    {
        var code = """
            using Orleans;

            namespace TestProject;

            [GenerateSerializer]
            public class DemoData
            {
                public string Value { get; set; } = string.Empty;
                public int Count { get; set; }
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var baselineResult = RunSourceGenerator(compilation);
        var configuredResult = RunSourceGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.orleans_generatefieldids"] = "PublicProperties",
            });

        Assert.Contains(baselineResult.Diagnostics, d => d.Id == DiagnosticRuleId.CanNotGenerateImplicitFieldIds);
        Assert.Empty(configuredResult.Diagnostics);
        Assert.Contains(configuredResult.GeneratedSources, source => source.HintName.Contains(".orleans.ser.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompatibilityInvokersOption_GeneratesAdditionalInheritedInvokables()
    {
        var code = """
            using Orleans;
            using System.Threading.Tasks;

            namespace TestProject;

            public interface IBaseGrain : IGrainWithIntegerKey
            {
                Task Ping();
            }

            public interface IDerivedGrain : IBaseGrain
            {
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var baselineResult = RunSourceGenerator(compilation);
        var configuredResult = RunSourceGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.orleansgeneratecompatibilityinvokers"] = "true",
            });

        Assert.Empty(baselineResult.Diagnostics);
        Assert.Empty(configuredResult.Diagnostics);

        var baselineSource = ConcatenateGeneratedSources(baselineResult);
        var configuredSource = ConcatenateGeneratedSources(configuredResult);

        Assert.True(
            CountGeneratedInvokableClasses(configuredSource) > CountGeneratedInvokableClasses(baselineSource),
            "Enabling compatibility invokers should generate additional invokable classes for inherited grain methods.");

        var compatibilityInvokers = configuredResult.GeneratedSources
            .Where(static source => source.HintName.Contains(".orleans.proxy.", StringComparison.Ordinal))
            .SelectMany(static source => CSharpSyntaxTree.ParseText(source.SourceText.ToString().TrimStart('\uFEFF'))
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .Where(static declaration => declaration.Identifier.ValueText.StartsWith("Invokable_IDerivedGrain_", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(compatibilityInvokers);

        var metadataSource = configuredResult.GeneratedSources
            .Single(source => source.HintName.EndsWith(".orleans.metadata.g.cs", StringComparison.Ordinal))
            .SourceText
            .ToString();
        Assert.Contains(
            "typeof(OrleansCodeGen.TestProject.Invokable_IDerivedGrain_",
            metadataSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityInvokersOption_ExplicitAliasSuppressesInheritedGeneratedAlias()
    {
        var baseline = await CreateCompilation(
            """
            using Orleans;
            using System.Threading.Tasks;

            public interface IBaseGrain : IGrainWithIntegerKey
            {
                Task Ping();
            }
            """,
            "Baseline");
        var baseMethod = Assert.Single(baseline.GetTypeByMetadataName("IBaseGrain")!.GetMembers().OfType<IMethodSymbol>());
        var legacyMethodId = GeneratedCodeUtilities.CreateHashedMethodId(baseMethod);
        var compilation = await CreateCompilation(
            $$"""
            using Orleans;
            using System.Threading;
            using System.Threading.Tasks;

            public interface IBaseGrain : IGrainWithIntegerKey
            {
                Task Ping();
            }

            public interface IDerivedGrain : IBaseGrain
            {
                [Alias("{{legacyMethodId}}")]
                Task Ping(CancellationToken cancellationToken);
            }
            """,
            "TestProject");

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken));
        var result = RunSourceGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.orleansgeneratecompatibilityinvokers"] = "true",
            });
        Assert.Empty(result.Diagnostics);

        var derivedInterface = compilation.GetTypeByMetadataName("IDerivedGrain")!;
        var cancellationMethod = Assert.Single(
            derivedInterface.GetMembers("Ping").OfType<IMethodSymbol>(),
            static method => method.Parameters.Length == 1);
        var cancellationGeneratedId = GeneratedCodeUtilities.CreateHashedMethodId(cancellationMethod);
        var generatedSource = ConcatenateGeneratedSources(result);
        var registration = Assert.Single(
            generatedSource.Split(Environment.NewLine),
            line => line.Contains($"Add(\"{legacyMethodId}\", typeof(", StringComparison.Ordinal)
                && line.Contains("Invokable_IDerivedGrain_", StringComparison.Ordinal));
        Assert.Contains(cancellationGeneratedId, registration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityInvokersOption_PreservesCrossNamespaceInvokables()
    {
        var code = """
            using Orleans;
            using System.Threading.Tasks;

            namespace A
            {
                public interface IFoo : IGrainWithIntegerKey
                {
                    Task Ping();
                }
            }

            namespace B
            {
                public interface IFoo : A.IFoo
                {
                }
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var result = RunSourceGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.orleansgeneratecompatibilityinvokers"] = "true",
            });

        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            result.GeneratedSources,
            static source => source.SourceText.ToString().Contains(
                "namespace OrleansCodeGen.B",
                StringComparison.Ordinal)
                && source.SourceText.ToString().Contains(
                    "class Invokable_IFoo_",
                    StringComparison.Ordinal));

        var outputCompilation = compilation.AddSyntaxTrees(
            result.GeneratedSources.Select(static source => CSharpSyntaxTree.ParseText(source.SourceText, path: source.HintName)));
        Assert.Empty(
            outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task ProviderMetadata_EmitsCompilableModuleInitializer()
    {
        var code = """
            using Orleans;

            [assembly: RegisterProvider("Test", "Clustering", "Client", typeof(TestProject.Provider))]

            namespace TestProject;

            public sealed class Provider
            {
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var result = RunSourceGenerator(compilation);
        var metadataSource = result.GeneratedSources
            .Single(source => source.HintName.EndsWith(".orleans.metadata.g.cs", StringComparison.Ordinal))
            .SourceText
            .ToString();

        Assert.Contains("global::System.Runtime.CompilerServices.ModuleInitializerAttribute", metadataSource, StringComparison.Ordinal);
        Assert.Contains("global::Orleans.Serialization.Configuration.ProviderMetadataRegistry.Register", metadataSource, StringComparison.Ordinal);

        var outputCompilation = compilation
            .AddReferences(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IConfigureOptions<>).Assembly.Location))
            .AddSyntaxTrees(
            result.GeneratedSources.Select(static source => CSharpSyntaxTree.ParseText(source.SourceText, path: source.HintName)));
        Assert.Empty(
            outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task AttachDebuggerFalseOption_DoesNotChangeOutput()
    {
        var code = """
            using Orleans;

            namespace TestProject;

            [GenerateSerializer]
            public class DemoData
            {
                [Id(0)]
                public string Value { get; set; } = string.Empty;
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var baselineResult = RunSourceGenerator(compilation);
        var configuredResult = RunSourceGenerator(
            compilation,
            new Dictionary<string, string>
            {
                ["build_property.orleans_attachdebugger"] = "false",
            });

        Assert.Empty(baselineResult.Diagnostics);
        Assert.Empty(configuredResult.Diagnostics);
        Assert.Equal(
            baselineResult.GeneratedSources.Select(source => source.HintName).OrderBy(name => name),
            configuredResult.GeneratedSources.Select(source => source.HintName).OrderBy(name => name));
        Assert.Equal(
            ConcatenateGeneratedSources(baselineResult),
            ConcatenateGeneratedSources(configuredResult));
    }

    [Fact]
    public async Task GeneratedSources_EmitMetadataLast()
    {
        var code = """
            using Orleans;
            using System.Threading.Tasks;

            namespace TestProject;

            [GenerateSerializer]
            public class DemoData
            {
                [Id(0)]
                public string Value { get; set; } = string.Empty;
            }

            public interface IMyGrain : IGrainWithIntegerKey
            {
                Task<DemoData> Get();
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var result = RunSourceGenerator(compilation);

        Assert.Empty(result.Diagnostics);

        var emittedHintNames = result.GeneratedSources
            .Where(static source => !string.IsNullOrWhiteSpace(source.SourceText.ToString()))
            .Select(static source => source.HintName)
            .ToArray();

        Assert.NotEmpty(emittedHintNames);
        Assert.Contains(emittedHintNames, static hintName => hintName.Contains(".orleans.ser.", StringComparison.Ordinal));
        Assert.Contains(emittedHintNames, static hintName => hintName.Contains(".orleans.proxy.", StringComparison.Ordinal));
        Assert.EndsWith(".orleans.metadata.g.cs", emittedHintNames[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedInvokableActivators_AreRegisteredInMetadata()
    {
        var code = """
            using System;
            using System.Threading.Tasks;
            using Orleans;
            using Orleans.Runtime;

            namespace TestProject;

            [InvokableBaseType(typeof(GrainReference), typeof(Task<>), typeof(ActivatingTaskRequest<>))]
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ActivatingAttribute : Attribute
            {
            }

            public abstract class ActivatingTaskRequest<TResult> : TaskRequest<TResult>
            {
                [GeneratedActivatorConstructor]
                protected ActivatingTaskRequest(IServiceProvider serviceProvider)
                {
                }
            }

            public interface IActivatingGrain : IGrainWithIntegerKey
            {
                [Activating]
                Task<string> GetValue();
            }

            public interface INormalGrain : IGrainWithIntegerKey
            {
                Task<string> GetValue();
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var result = RunSourceGenerator(compilation);

        Assert.Empty(result.Diagnostics);

        var emittedActivatorNames = GetGeneratedClassNames(result, ".orleans.proxy.", "Activator_Invokable_");
        var registeredActivatorNames = GetRegisteredGeneratedInvokableActivatorNames(result);

        Assert.Single(emittedActivatorNames);
        Assert.Equal(emittedActivatorNames, registeredActivatorNames);
        Assert.Contains(emittedActivatorNames, static name => name.Contains("IActivatingGrain", StringComparison.Ordinal));
        Assert.DoesNotContain(registeredActivatorNames, static name => name.Contains("INormalGrain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordPrimaryConstructorFieldIds_AreNotDuplicatedOrDropped()
    {
        var code = """
            using Orleans;

            namespace TestProject;

            [GenerateSerializer]
            public sealed record PrimaryCtorRecord(
                [property: Id(0)] string Value,
                [field: Id(1)] int Count)
            {
                [Id(2)]
                public string Extra { get; init; } = string.Empty;
            }
            """;

        var compilation = await CreateCompilation(code, "TestProject");
        var generator = new OrleansSerializationSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            optionsProvider: TestCompilationHelper.CreateOptionsProvider(),
            driverOptions: new GeneratorDriverOptions(default));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics,
            TestContext.Current.CancellationToken);
        var result = driver.GetRunResult().Results.Single();

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Diagnostics);
        Assert.Empty(
            outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var serializerSource = Assert.Single(
            result.GeneratedSources,
            static source => source.HintName.Contains(".orleans.ser.", StringComparison.Ordinal)
                && source.SourceText.ToString().Contains("Codec_PrimaryCtorRecord", StringComparison.Ordinal));
        var serializerText = serializerSource.SourceText.ToString();

        Assert.Equal(1, CountOccurrences(serializerText, "instance.Value"));
        Assert.Equal(1, CountOccurrences(serializerText, "instance.Count"));
        Assert.Equal(1, CountOccurrences(serializerText, "instance.Extra"));
        Assert.Equal(1, CountOccurrences(serializerText, "if (id == 0U)"));
        Assert.Equal(1, CountOccurrences(serializerText, "if (id == 1U)"));
        Assert.Equal(1, CountOccurrences(serializerText, "if (id == 2U)"));
    }

    private static GeneratorRunResult RunSourceGenerator(
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var generator = new OrleansSerializationSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            optionsProvider: TestCompilationHelper.CreateOptionsProvider(globalOptions),
            driverOptions: new GeneratorDriverOptions(default));
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Results.Single();
    }

    /// <summary>
    /// 对所提供的代码运行 Orleans 源码生成器的辅助方法，
    /// 并验证生成成功且没有错误。
    /// 使用快照测试验证生成的代码是否符合预期。
    /// </summary>
    private static async Task AssertSuccessfulSourceGeneration(string code, string? snapshotName = null)
    {
        var projectName = "TestProject";
        var compilation = await CreateCompilation(code, projectName);
        Assert.Empty(compilation.GetDiagnostics());
        var result = RunSourceGenerator(compilation);
        Assert.Empty(result.Diagnostics);

        Assert.NotEmpty(result.GeneratedSources);
        Assert.All(result.GeneratedSources, generated =>
            Assert.StartsWith($"{projectName}.orleans.", generated.HintName, StringComparison.Ordinal));
        var generatedSource = ConcatenateGeneratedSources(result);

        var snapshot = Verify(generatedSource, extension: "cs").UseDirectory("snapshots");
        if (snapshotName is not null)
        {
            var supportsGenericAccessors = SourceGeneratorOptionsParser.ParseOptions(TestCompilationHelper.CreateOptionsProvider().GlobalOptions).SupportsGenericUnsafeAccessors;
            snapshot = snapshot.UseFileName($"{nameof(OrleansSourceGeneratorTests)}.{snapshotName}.{(supportsGenericAccessors ? "UnsafeAccessor" : "FieldAccessor")}");
        }

        await snapshot;
    }

    private static string ConcatenateGeneratedSources(GeneratorRunResult result)
    {
        var assemblyAttributes = new List<AttributeListSyntax>();
        var topLevelMembers = new List<MemberDeclarationSyntax>();
        var namespaces = new Dictionary<string, NamespaceMembers>(StringComparer.Ordinal);

        foreach (var item in result.GeneratedSources
            .Select(static (source, index) => (Source: source, Index: index))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Source.SourceText.ToString()))
            .OrderBy(static item => GetSnapshotSourceOrder(item.Source.HintName))
            .ThenBy(static item => item.Index))
        {
            var root = CSharpSyntaxTree.ParseText(item.Source.SourceText.ToString().TrimStart('\uFEFF').TrimEnd()).GetCompilationUnitRoot();
            assemblyAttributes.AddRange(root.AttributeLists);

            foreach (var member in root.Members)
            {
                if (member is NamespaceDeclarationSyntax namespaceDeclaration)
                {
                    var namespaceName = namespaceDeclaration.Name.ToString();
                    if (!namespaces.TryGetValue(namespaceName, out var namespaceMembers))
                    {
                        namespaceMembers = new NamespaceMembers(namespaceDeclaration.Usings);
                        namespaces.Add(namespaceName, namespaceMembers);
                    }

                    namespaceMembers.Members.AddRange(namespaceDeclaration.Members);
                }
                else
                {
                    topLevelMembers.Add(member);
                }
            }
        }

        var combinedMembers = new List<MemberDeclarationSyntax>();
        combinedMembers.AddRange(topLevelMembers);
        foreach (var pair in namespaces.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            combinedMembers.Add(
                SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(pair.Key))
                    .WithUsings(pair.Value.Usings)
                    .WithMembers(SyntaxFactory.List(OrderLegacyGeneratedMembers(pair.Value.Members))));
        }

        var unit = SyntaxFactory.CompilationUnit()
            .WithAttributeLists(SyntaxFactory.List(assemblyAttributes))
            .WithMembers(SyntaxFactory.List(combinedMembers));
        var resultText = unit.NormalizeWhitespace().ToFullString();
        resultText = resultText.Replace(".Add(typeof(int));", ".Add(typeof( int ));", StringComparison.Ordinal);
        if (assemblyAttributes.Count > 0)
        {
            resultText += $"{Environment.NewLine}#pragma warning restore";
        }

        return resultText;

        static int GetSnapshotSourceOrder(string hintName)
        {
            if (hintName.Contains(".orleans.proxy.", StringComparison.Ordinal))
            {
                return 0;
            }

            if (hintName.Contains(".orleans.ser.", StringComparison.Ordinal))
            {
                return 1;
            }

            if (hintName.EndsWith(".orleans.metadata.g.cs", StringComparison.Ordinal))
            {
                return 3;
            }

            return 2;
        }
    }

    private static List<MemberDeclarationSyntax> OrderLegacyGeneratedMembers(List<MemberDeclarationSyntax> members)
    {
        var serializerOrder = GetSerializerOrder(members);
        return members
            .Select(static (member, index) => (Member: member, Index: index))
            .OrderBy(static item => GetLegacyMemberCategory(item.Member))
            .ThenBy(item => GetSerializerOrderIndex(item.Member, serializerOrder))
            .ThenBy(static item => GetSerializerMemberKindOrder(item.Member))
            .ThenBy(static item => item.Index)
            .Select(static item => item.Member)
            .ToList();
    }

    private static Dictionary<string, int> GetSerializerOrder(List<MemberDeclarationSyntax> members)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var metadataClass in members
            .OfType<ClassDeclarationSyntax>()
            .Where(static declaration => declaration.Identifier.ValueText.StartsWith("Metadata_", StringComparison.Ordinal)))
        {
            foreach (var invocation in metadataClass.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "AddSerializer"
                    }
                    || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not TypeOfExpressionSyntax typeOfExpression)
                {
                    continue;
                }

                var serializerClassName = GetGeneratedClassIdentifier(typeOfExpression.Type.ToString().Split('.').Last());
                if (serializerClassName.StartsWith("Codec_", StringComparison.Ordinal))
                {
                    result.TryAdd(serializerClassName["Codec_".Length..], result.Count);
                }
            }
        }

        return result;
    }

    private static string GetGeneratedClassIdentifier(string typeName)
    {
        var genericMarkerIndex = typeName.IndexOf('<');
        return genericMarkerIndex >= 0 ? typeName[..genericMarkerIndex] : typeName;
    }

    private static int GetLegacyMemberCategory(MemberDeclarationSyntax member)
    {
        if (member is not ClassDeclarationSyntax classDeclaration)
        {
            return 1;
        }

        var className = classDeclaration.Identifier.ValueText;
        if (className.StartsWith("Invokable_", StringComparison.Ordinal)
            || className.StartsWith("Proxy_", StringComparison.Ordinal))
        {
            return 0;
        }

        if (className.StartsWith("Metadata_", StringComparison.Ordinal))
        {
            return 3;
        }

        return 1;
    }

    private static int GetSerializerOrderIndex(MemberDeclarationSyntax member, Dictionary<string, int> serializerOrder)
    {
        if (member is not ClassDeclarationSyntax classDeclaration)
        {
            return int.MaxValue;
        }

        var className = classDeclaration.Identifier.ValueText;
        var serializableTypeName = GetSerializableTypeName(className);
        if (serializableTypeName is not null && serializerOrder.TryGetValue(serializableTypeName, out var order))
        {
            return order;
        }

        return int.MaxValue;
    }

    private static int GetSerializerMemberKindOrder(MemberDeclarationSyntax member)
    {
        if (member is not ClassDeclarationSyntax classDeclaration)
        {
            return 0;
        }

        var className = classDeclaration.Identifier.ValueText;
        if (className.StartsWith("Codec_", StringComparison.Ordinal))
        {
            return 0;
        }

        if (className.StartsWith("Copier_", StringComparison.Ordinal))
        {
            return 1;
        }

        if (className.StartsWith("Activator_", StringComparison.Ordinal))
        {
            return 2;
        }

        return 0;
    }

    private static string? GetSerializableTypeName(string generatedClassName)
    {
        if (generatedClassName.StartsWith("Codec_", StringComparison.Ordinal))
        {
            return generatedClassName["Codec_".Length..];
        }

        if (generatedClassName.StartsWith("Copier_", StringComparison.Ordinal))
        {
            return generatedClassName["Copier_".Length..];
        }

        if (generatedClassName.StartsWith("Activator_", StringComparison.Ordinal))
        {
            return generatedClassName["Activator_".Length..];
        }

        return null;
    }

    private static int CountGeneratedInvokableClasses(string source)
        => source.Split(Environment.NewLine)
            .Count(line => line.Contains("public sealed class Invokable_", StringComparison.Ordinal));

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    private static string[] GetGeneratedClassNames(GeneratorRunResult result, string hintNameFragment, string classNamePrefix)
        => result.GeneratedSources
            .Where(source => source.HintName.Contains(hintNameFragment, StringComparison.Ordinal))
            .SelectMany(static source => CSharpSyntaxTree.ParseText(source.SourceText.ToString().TrimStart('\uFEFF')).GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .Select(static declaration => declaration.Identifier.ValueText)
            .Where(name => name.StartsWith(classNamePrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static string[] GetRegisteredGeneratedInvokableActivatorNames(GeneratorRunResult result)
        => result.GeneratedSources
            .Where(static source => source.HintName.EndsWith(".orleans.metadata.g.cs", StringComparison.Ordinal))
            .SelectMany(static source => CSharpSyntaxTree.ParseText(source.SourceText.ToString().TrimStart('\uFEFF')).GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            .Where(static invocation =>
                invocation.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "AddActivator"
                })
            .Select(static invocation => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .Select(static typeOfExpression => GetGeneratedClassIdentifier(typeOfExpression.Type.ToString().Split('.').Last()))
            .Where(static name => name.StartsWith("Activator_Invokable_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private sealed class NamespaceMembers(SyntaxList<UsingDirectiveSyntax> usings)
    {
        public SyntaxList<UsingDirectiveSyntax> Usings { get; } = usings;

        public List<MemberDeclarationSyntax> Members { get; } = [];
    }

    /// <summary>
    /// 创建具有必要 Orleans 引用的 Roslyn 编译。
    /// 这模拟了源生成器运行时的构建环境，
    /// 包含所有必需的 Orleans 程序集及 .NET 框架引用。
    /// </summary>
    private static Task<CSharpCompilation> CreateCompilation(string sourceCode, string assemblyName = "TestProject")
        => TestCompilationHelper.CreateCompilation(sourceCode, assemblyName);
}
