namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述一个众所周知的ID型号绘图。
/// </summary>
internal readonly record struct WellKnownTypeIdModel(TypeRef Type, uint Id);

/// <summary>
/// 描述一种别名绘图类型。
/// </summary>
internal readonly record struct TypeAliasModel(TypeRef Type, string Alias);

/// <summary>
/// 复合型别名路径中的单个部件 。
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
/// 描述复合类型别名条目(部件图绘制到某类型的路径)。
/// </summary>
internal readonly record struct CompoundTypeAliasModel(EquatableArray<CompoundAliasComponentModel> Components, TypeRef TargetType);

/// <summary>
/// 描述接口的实施(一种具体类型,即可解释界面的实施)。
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
/// 描述供应商登记。
/// </summary>
internal readonly record struct RegisteredProviderModel(string Target, string Kind, string Name, TypeRef Type);

/// <summary>
/// 通过 <c>[GenerateCodeForDeclaringAssembly]</c>从引用组件中提取的汇总数据
/// 和 <c>[ApplicationPart]</c> 属性。此模型由 <c>CompilationProvider</c>- 以基于
/// 并通过结构平等缓存管道。
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
