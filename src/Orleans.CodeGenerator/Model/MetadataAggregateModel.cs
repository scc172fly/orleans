namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 聚合所有管道输出的组合模型，用于生成元数据类。
/// 这是最终 <c>RegisterImplementationSourceOutput</c> 的输入，该步骤生成
/// <c>Metadata_{AssemblyName}</c> 类和程序集级特性。
/// </summary>
internal sealed record class MetadataAggregateModel(
    string AssemblyName,
    EquatableArray<SerializableTypeModel> SerializableTypes,
    EquatableArray<ProxyInterfaceModel> ProxyInterfaces,
    EquatableArray<RegisteredCodecModel> RegisteredCodecs,
    ReferenceAssemblyModel ReferenceAssemblyData,
    EquatableArray<TypeRef> ActivatableTypes,
    EquatableArray<TypeRef> GeneratedProxyTypes,
    EquatableArray<TypeRef> InvokableInterfaces,
    EquatableArray<string> GeneratedInvokableMetadataNames,
    EquatableArray<string> GeneratedInvokableActivatorMetadataNames,
    EquatableArray<CompoundTypeAliasModel> GeneratedCompoundTypeAliases,
    EquatableArray<InterfaceImplementationModel> InterfaceImplementations,
    EquatableArray<DefaultCopierModel> DefaultCopiers);

/// <summary>
/// 描述可浅复制类型的默认复制器映射（类型 → 复制器类型）。
/// </summary>
internal readonly record struct DefaultCopierModel(TypeRef OriginalType, TypeRef CopierType);
