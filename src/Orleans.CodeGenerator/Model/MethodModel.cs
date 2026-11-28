namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述用于生成可调用对象或代理的方法参数。
/// </summary>
internal readonly record struct MethodParameterModel(
    string Name,
    TypeRef Type,
    int Ordinal,
    bool IsCancellationToken,
    uint? ExplicitFieldId);

/// <summary>
/// 描述用于生成可调用对象的代理接口方法。
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
/// 描述与可调用方法特性关联的自定义初始化器方法。
/// </summary>
internal readonly record struct CustomInitializerModel(string MethodName, string ArgumentValue);
