using Orleans.CodeGenerator.Model;

namespace Orleans.CodeGenerator;

/// <summary>
/// 配置序列化器及 RPC 代码生成。
/// </summary>
public class CodeGeneratorOptions
{
    /// <summary>
    /// <c>Orleans.IdAttribute</c> 的元数据名称。
    /// </summary>
    public const string IdAttribute = "Orleans.IdAttribute";

    /// <summary>
    /// <c>Orleans.AliasAttribute</c> 的元数据名称。
    /// </summary>
    public const string AliasAttribute = "Orleans.AliasAttribute";

    /// <summary>
    /// <c>Orleans.ImmutableAttribute</c> 的元数据名称。
    /// </summary>
    public const string ImmutableAttribute = "Orleans.ImmutableAttribute";

    /// <summary>
    /// 用于标识反序列化期间所用构造函数的特性的元数据名称。
    /// </summary>
    public static readonly IReadOnlyList<string> ConstructorAttributes = ["Orleans.OrleansConstructorAttribute", "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute"];

    /// <summary>
    /// 获取或设置为可序列化成员生成字段标识符的策略。
    /// </summary>
    public GenerateFieldIds GenerateFieldIds { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示是否为继承的 RPC 方法生成兼容性调用器。
    /// </summary>
    public bool GenerateCompatibilityInvokers { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示生成的序列化器和复制器是否支持使用 .NET 热重载添加成员。
    /// </summary>
    public bool HotReloadSafe { get; set; }

    internal bool SupportsUnsafeAccessors { get; set; }
    internal bool SupportsGenericUnsafeAccessors { get; set; }
    internal bool SupportsVolatileUnsafeAccessors { get; set; }
}
