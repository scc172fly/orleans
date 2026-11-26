using System;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.TestKit;
using Xunit;

namespace Orleans.Serialization.UnitTests;

/// <summary>
/// Orleans中基于转换器的序列机制测试。
/// 
/// 转换器允许 Orleans 从外部图书馆中序列化无法修改的类型
/// 添加 Orleans 序列化属性。通过代用类型实现这一点,即:
/// - 作为序列的中间人
/// - 转换外国类型和可连序代表制
/// - 支持参考类型和价值类型
/// - 允许外国图书馆衍生品种类的序列化
/// 
/// 这种办法使 Orleans 能够保持高绩效连续连续处理,而
/// 与第三方图书馆和遗产法相结合。
/// </summary>
public class ConverterCodecTests : FieldCodecTester<MyForeignLibraryType, IFieldCodec<MyForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public ConverterCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MyForeignLibraryType CreateValue() => new(12, "hi", DateTimeOffset.Now);
    protected override bool Equals(MyForeignLibraryType? left, MyForeignLibraryType? right) => ReferenceEquals(left, right) || left!.Equals(right);
    protected override MyForeignLibraryType[] TestValues => new MyForeignLibraryType[] { null!, CreateValue() };
}

public class ConverterCopierTests : CopierTester<MyForeignLibraryType, IDeepCopier<MyForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public ConverterCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MyForeignLibraryType CreateValue() => new(12, "hi", DateTimeOffset.Now);
    protected override bool Equals(MyForeignLibraryType? left, MyForeignLibraryType? right) => ReferenceEquals(left, right) || left!.Equals(right);
    protected override MyForeignLibraryType[] TestValues => new MyForeignLibraryType[] { null!, CreateValue() };
}

public class WrappedConverterCodecTests : FieldCodecTester<WrapsMyForeignLibraryType, IFieldCodec<WrapsMyForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public WrappedConverterCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override WrapsMyForeignLibraryType CreateValue() => new() { IntValue = 12, ForeignValue = new MyForeignLibraryType(12, "hi", DateTimeOffset.Now), OtherIntValue = 7468249 };
    protected override bool Equals(WrapsMyForeignLibraryType? left, WrapsMyForeignLibraryType? right) => ReferenceEquals(left, right) || left!.Equals(right);
    protected override WrapsMyForeignLibraryType[] TestValues => new WrapsMyForeignLibraryType[] { default!, CreateValue() };
}

public class WrappedConverterCopierTests : CopierTester<WrapsMyForeignLibraryType, IDeepCopier<WrapsMyForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public WrappedConverterCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override WrapsMyForeignLibraryType CreateValue() => new() { IntValue = 12, ForeignValue = new MyForeignLibraryType(12, "hi", DateTimeOffset.Now), OtherIntValue = 7468249 };
    protected override bool Equals(WrapsMyForeignLibraryType? left, WrapsMyForeignLibraryType? right) => ReferenceEquals(left, right) || left!.Equals(right);
    protected override WrapsMyForeignLibraryType[] TestValues => new WrapsMyForeignLibraryType[] { default!, CreateValue() };
}

public class StructConverterCodecTests : ValueTypeFieldCodecTester<MyForeignLibraryValueType, IFieldCodec<MyForeignLibraryValueType>>, IClassFixture<SerializationTesterFixture>
{
    public StructConverterCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MyForeignLibraryValueType CreateValue() => new(12, "hi", DateTimeOffset.Now);
    protected override bool Equals(MyForeignLibraryValueType left, MyForeignLibraryValueType right) => left.Equals(right);
    protected override MyForeignLibraryValueType[] TestValues => new MyForeignLibraryValueType[] { default, CreateValue() };
}

public class StructConverterCopierTests : CopierTester<MyForeignLibraryValueType, IDeepCopier<MyForeignLibraryValueType>>, IClassFixture<SerializationTesterFixture>
{
    public StructConverterCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MyForeignLibraryValueType CreateValue() => new(12, "hi", DateTimeOffset.Now);
    protected override bool Equals(MyForeignLibraryValueType left, MyForeignLibraryValueType right) => left.Equals(right);
    protected override MyForeignLibraryValueType[] TestValues => new MyForeignLibraryValueType[] { default, CreateValue() };
}

public class WrappedStructConverterCodecTests : ValueTypeFieldCodecTester<WrapsMyForeignLibraryValueType, IFieldCodec<WrapsMyForeignLibraryValueType>>, IClassFixture<SerializationTesterFixture>
{
    public WrappedStructConverterCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override WrapsMyForeignLibraryValueType CreateValue() => new() { IntValue = 12, ForeignValue = new MyForeignLibraryValueType(12, "hi", DateTimeOffset.Now), OtherIntValue = 7468249 };
    protected override bool Equals(WrapsMyForeignLibraryValueType left, WrapsMyForeignLibraryValueType right) => left.Equals(right);
    protected override WrapsMyForeignLibraryValueType[] TestValues => new WrapsMyForeignLibraryValueType[] { default, CreateValue() };
}

public class WrappedStructConverterCopierTests : CopierTester<WrapsMyForeignLibraryValueType, IDeepCopier<WrapsMyForeignLibraryValueType>>, IClassFixture<SerializationTesterFixture>
{
    public WrappedStructConverterCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override WrapsMyForeignLibraryValueType CreateValue() => new() { IntValue = 12, ForeignValue = new MyForeignLibraryValueType(12, "hi", DateTimeOffset.Now), OtherIntValue = 7468249 };
    protected override bool Equals(WrapsMyForeignLibraryValueType left, WrapsMyForeignLibraryValueType right) => left.Equals(right);
    protected override WrapsMyForeignLibraryValueType[] TestValues => new WrapsMyForeignLibraryValueType[] { default, CreateValue() };
}

public class DerivedConverterCodecTests : FieldCodecTester<DerivedFromMyForeignLibraryType, IFieldCodec<DerivedFromMyForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public DerivedConverterCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override DerivedFromMyForeignLibraryType CreateValue() => new(658, 12, "hi", DateTimeOffset.Now);
    protected override bool Equals(DerivedFromMyForeignLibraryType? left, DerivedFromMyForeignLibraryType? right) => ReferenceEquals(left, right) || left!.Equals(right);
    protected override DerivedFromMyForeignLibraryType[] TestValues => new DerivedFromMyForeignLibraryType[] { null!, CreateValue() };
}

public class DerivedConverterCopierTests : CopierTester<DerivedFromMyForeignLibraryType, IDeepCopier<DerivedFromMyForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public DerivedConverterCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override DerivedFromMyForeignLibraryType CreateValue() => new(658, 12, "hi", DateTimeOffset.Now);
    protected override bool Equals(DerivedFromMyForeignLibraryType? left, DerivedFromMyForeignLibraryType? right) => ReferenceEquals(left, right) || left!.Equals(right);
    protected override DerivedFromMyForeignLibraryType[] TestValues => new DerivedFromMyForeignLibraryType[] { null!, CreateValue() };
}


public class CombinedConverterCopierTests : CopierTester<MyFirstForeignLibraryType, IDeepCopier<MyFirstForeignLibraryType>>, IClassFixture<SerializationTesterFixture>
{
    public CombinedConverterCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
    {
    }

    protected override MyFirstForeignLibraryType CreateValue() => new() { Num = 12, String = "hi", DateTimeOffset = DateTimeOffset.Now };
    protected override bool Equals(MyFirstForeignLibraryType? left, MyFirstForeignLibraryType? right) => left!.Equals(right);
    protected override MyFirstForeignLibraryType[] TestValues => new MyFirstForeignLibraryType[] { CreateValue() };
}
