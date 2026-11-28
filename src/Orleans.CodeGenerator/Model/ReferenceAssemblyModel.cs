namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述已知类型 ID 映射。
/// </summary>
internal readonly record struct WellKnownTypeIdModel(TypeRef Type, uint Id);

/// <summary>
/// 描述类型别名映射。
/// </summary>
internal readonly record struct TypeAliasModel(TypeRef Type, string Alias);

/// <summary>
/// 复合类型别名路径中的单个组成部分。
/// </summary>
internal readonly record struct CompoundAliasComponentModel
{
    public CompoundAliasComponentModel(string stringValue)
    {
        StringValue = stringValue;
        TypeValue = TypeRef.Empty;
        IsType = false;
    }

    public CompoundAliasComponentModel(TypeRef typeValue)
    {
        StringValue = null;
        TypeValue = typeValue;
        IsType = true;
    }

    public bool IsString => !IsType && StringValue is not null;
    public bool IsType { get; }
    public string? StringValue { get; }
    public TypeRef TypeValue { get; }

}

/// <summary>
/// 描述复合类型别名条目（映射到某类型的组成部分路径）。
/// </summary>
internal readonly record struct CompoundTypeAliasModel(EquatableArray<CompoundAliasComponentModel> Components, TypeRef TargetType);

/// <summary>
/// 描述接口实现（实现可调用接口的具体类型）。
/// </summary>
internal readonly record struct InterfaceImplementationModel
{
    public InterfaceImplementationModel(TypeRef implementationType, SourceLocationModel sourceLocation = default)
    {
        ImplementationType = implementationType;
        SourceLocation = sourceLocation;
    }

    public TypeRef ImplementationType { get; }
    public SourceLocationModel SourceLocation { get; }
}

/// <summary>
/// 描述提供程序注册。
/// </summary>
internal readonly record struct RegisteredProviderModel(string Target, string Kind, string Name, TypeRef Type);

/// <summary>
/// 通过 <c>[GenerateCodeForDeclaringAssembly]</c>
/// 和 <c>[ApplicationPart]</c> 特性从引用程序集提取的聚合数据。此模型由基于 <c>CompilationProvider</c> 的
/// 管道生成，并通过结构相等性缓存。
/// </summary>
internal sealed record class ReferenceAssemblyModel(
    string AssemblyName,
    EquatableArray<string> ApplicationParts,
    EquatableArray<WellKnownTypeIdModel> WellKnownTypeIds,
    EquatableArray<TypeAliasModel> TypeAliases,
    EquatableArray<CompoundTypeAliasModel> CompoundTypeAliases,
    EquatableArray<SerializableTypeModel> ReferencedSerializableTypes,
    EquatableArray<ProxyInterfaceModel> ReferencedProxyInterfaces,
    EquatableArray<RegisteredCodecModel> RegisteredCodecs,
    EquatableArray<RegisteredProviderModel> RegisteredProviders,
    EquatableArray<InterfaceImplementationModel> InterfaceImplementations);
