namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述用于 RPC 代理生成的代理基类型。
/// </summary>
internal sealed record class ProxyBaseModel(
    TypeRef ProxyBaseType,
    bool IsExtension,
    string GeneratedClassNameComponent,
    TypeMetadataIdentity MetadataIdentity = default);

/// <summary>
/// 描述使用 <c>[GenerateMethodSerializers]</c> 标注的接口，用于增量生成代理及可调用对象。
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
