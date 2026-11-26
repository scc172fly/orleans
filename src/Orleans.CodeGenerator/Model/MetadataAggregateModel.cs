namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 综合模型,汇总元数据类生成的所有编审中产出。
/// 这是对最后 <c>RegisterImplementationSourceOutput</c> 生产可生产
/// <c>Metadata_{AssemblyName}</c> 类和组级属性。
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
/// 描述为浅可复制型的默认复印机映射(类型为“复印机”类型)。
/// </summary>
internal readonly record struct DefaultCopierModel(TypeRef OriginalType, TypeRef CopierType);
