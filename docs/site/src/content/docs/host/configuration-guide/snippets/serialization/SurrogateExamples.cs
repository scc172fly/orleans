using Orleans;

namespace Orleans.Docs.Snippets.Serialization;

// <surrogate_value_type>
// 这是外国类型, 您无法控制 。
public struct MyForeignLibraryValueType
{
    public MyForeignLibraryValueType(int num, string str, DateTimeOffset dto)
    {
        Num = num;
        String = str;
        DateTimeOffset = dto;
    }

    public int Num { get; }
    public string String { get; }
    public DateTimeOffset DateTimeOffset { get; }
}

// 这是替代物, 将充当外国类型的替身。
// 代孕国应使用平原字段而不是特性来改进性能。
[GenerateSerializer]
public struct MyForeignLibraryValueTypeSurrogate
{
    [Id(0)]
    public int Num;

    [Id(1)]
    public string String;

    [Id(2)]
    public DateTimeOffset DateTimeOffset;
}

// 这是在代位和外国类型之间转换的转换器。
[RegisterConverter]
public sealed class MyForeignLibraryValueTypeSurrogateConverter :
    IConverter<MyForeignLibraryValueType, MyForeignLibraryValueTypeSurrogate>
{
    public MyForeignLibraryValueType ConvertFromSurrogate(
        in MyForeignLibraryValueTypeSurrogate surrogate) =>
        new(surrogate.Num, surrogate.String, surrogate.DateTimeOffset);

    public MyForeignLibraryValueTypeSurrogate ConvertToSurrogate(
        in MyForeignLibraryValueType value) =>
        new()
        {
            Num = value.Num,
            String = value.String,
            DateTimeOffset = value.DateTimeOffset
        };
}
// </surrogate_value_type>

// <surrogate_class_with_populator>
// 外国类型不密封,允许其他类型继承。
public class MyForeignLibraryType
{
    public MyForeignLibraryType() { }

    public MyForeignLibraryType(int num, string? str, DateTimeOffset dto)
    {
        Num = num;
        String = str;
        DateTimeOffset = dto;
    }

    public int Num { get; set; }
    public string? String { get; set; }
    public DateTimeOffset DateTimeOffset { get; set; }
}

// 代位人的定义与上一个例子中的定义相同。
[GenerateSerializer]
public struct MyForeignLibraryTypeSurrogate
{
    [Id(0)]
    public int Num;

    [Id(1)]
    public string? String;

    [Id(2)]
    public DateTimeOffset DateTimeOffset;
}

// 在转换器上安装 Iconverter 和 IPopulator 接口。
[RegisterConverter]
public sealed class MyForeignLibraryTypeSurrogateConverter :
    IConverter<MyForeignLibraryType, MyForeignLibraryTypeSurrogate>,
    IPopulator<MyForeignLibraryType, MyForeignLibraryTypeSurrogate>
{
    public MyForeignLibraryType ConvertFromSurrogate(
        in MyForeignLibraryTypeSurrogate surrogate) =>
        new(surrogate.Num, surrogate.String, surrogate.DateTimeOffset);

    public MyForeignLibraryTypeSurrogate ConvertToSurrogate(
        in MyForeignLibraryType value) =>
        new()
    {
        Num = value.Num,
        String = value.String,
        DateTimeOffset = value.DateTimeOffset
    };

    public void Populate(
        in MyForeignLibraryTypeSurrogate surrogate, MyForeignLibraryType value)
    {
        value.Num = surrogate.Num;
        value.String = surrogate.String;
        value.DateTimeOffset = surrogate.DateTimeOffset;
    }
}

// 申请类型可以继承外国类型的申请,但前提是这些申请类型不密封
// 自 Orleans 知道如何对它进行序列分类以来。
[GenerateSerializer]
public sealed class DerivedFromMyForeignLibraryType : MyForeignLibraryType
{
    public DerivedFromMyForeignLibraryType() { }

    public DerivedFromMyForeignLibraryType(
        int intValue, int num, string? str, DateTimeOffset dto) : base(num, str, dto)
    {
        IntValue = intValue;
    }

    [Id(0)]
    public int IntValue { get; set; }
}
// </surrogate_class_with_populator>
