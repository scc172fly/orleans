namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述代理服务器代理生成所使用的代理基类型 。
/// </summary>
internal sealed record class ProxyBaseModel(
    TypeRef ProxyBaseType,
    bool IsExtension,
    string GeneratedClassNameComponent,
    TypeMetadataIdentity MetadataIdentity = default);

/// <summary>
/// 描述a <c>[GenerateMethodSerializers]</c>- 递增代用/可宣传的一代附加说明的界面。
/// </summary>
internal sealed record class ProxyInterfaceModel(
    TypeRef InterfaceType,
    string Name,
    string GeneratedNamespace,
    EquatableArray<TypeParameterModel> TypeParameters,
    ProxyBaseModel ProxyBase,
    EquatableArray<MethodModel> Methods,
    SourceLocationModel SourceLocation = default,
    TypeMetadataIdentity MetadataIdentity = default);
