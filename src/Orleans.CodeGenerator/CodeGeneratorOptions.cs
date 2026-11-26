using Orleans.CodeGenerator.Model;

namespace Orleans.CodeGenerator;

/// <summary>
/// 配置序列器和 RPC 代码生成。
/// </summary>
public class CodeGeneratorOptions
{
    /// <summary>
    /// <c>Orleans.IdAttribute</c>的元数据名称。
    /// </summary>
    public const string IdAttribute = "Orleans.IdAttribute";

    /// <summary>
    /// <c>Orleans.AliasAttribute</c>的元数据名称。
    /// </summary>
    public const string AliasAttribute = "Orleans.AliasAttribute";

    /// <summary>
    /// <c>Orleans.ImmutableAttribute</c>的元数据名称。
    /// </summary>
    public const string ImmutableAttribute = "Orleans.ImmutableAttribute";

    /// <summary>
    /// 用于识别在解除飞行过程中使用的构造器的属性的元数据名称。
    /// </summary>
    public static readonly IReadOnlyList<string> ConstructorAttributes = ["Orleans.OrleansConstructorAttribute", "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute"];

    /// <summary>
    /// 获取或设置用于为可连号成员生成实地识别资料的战略。
    /// </summary>
    public GenerateFieldIds GenerateFieldIds { get; set; }

    /// <summary>
    /// 获取或设定一个数值,以显示是否为遗留的RPC方法生成兼容性参数。
    /// </summary>
    public bool GenerateCompatibilityInvokers { get; set; }

    /// <summary>
    /// 获取或设定一个值,显示生成的序列器和复印机是否支持使用 .NET 热重装入添加成员。
    /// </summary>
    public bool HotReloadSafe { get; set; }

    internal bool SupportsUnsafeAccessors { get; set; }
    internal bool SupportsGenericUnsafeAccessors { get; set; }
    internal bool SupportsVolatileUnsafeAccessors { get; set; }
}
