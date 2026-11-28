using Orleans;

namespace Orleans.Docs.Snippets.Serialization;

// <surrogate_value_type>
// 这是无法由你控制的外部类型。
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

// 这是用于替代外部类型的代理类型（surrogate）。
// 为获得更好的性能，代理类型应使用普通字段而非属性。
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

// 这是在代理类型与外部类型之间进行转换的转换器。
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
// 外部类型未密封，允许其他类型继承它。
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

// 代理类型的定义与前一个示例相同。
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

// 在转换器上实现 IConverter 和 IPopulator 接口。
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

// 如果外部类型未密封，应用程序类型就可以继承它，
// 因为 Orleans 知道如何序列化它。
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
