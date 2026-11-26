namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述可解释/代用生成的方法参数。
/// </summary>
internal readonly record struct MethodParameterModel(
    string Name,
    TypeRef Type,
    int Ordinal,
    bool IsCancellationToken,
    uint? ExplicitFieldId);

/// <summary>
/// 描述代用界面中可引用生成的代理界面的方法 。
/// </summary>
internal sealed record class MethodModel(
    string Name,
    TypeRef ReturnType,
    EquatableArray<MethodParameterModel> Parameters,
    EquatableArray<TypeParameterModel> TypeParameters,
    TypeRef ContainingInterfaceType,
    TypeRef OriginalContainingInterfaceType,
    string ContainingInterfaceName,
    string ContainingInterfaceGeneratedNamespace,
    int ContainingInterfaceTypeParameterCount,
    string GeneratedMethodId,
    string MethodId,
    string? ClaimedGeneratedMethodId,
    long? ResponseTimeoutTicks,
    EquatableArray<CustomInitializerModel> CustomInitializerMethods,
    bool IsCancellable)
{
    public bool HasAlias => !string.Equals(MethodId, GeneratedMethodId, StringComparison.Ordinal);
}

/// <summary>
/// 描述自定义初始化器方法, 与可引用方法属性相关联。
/// </summary>
internal readonly record struct CustomInitializerModel(string MethodName, string ArgumentValue);
