using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述一个 <c>[GenerateSerializer]</c>-- -- 附加说明的递增发现和元数据生成类型。
/// 序列式制导器、复印机和活性物排放解决了与当前汇编相比更多的语义依赖。
/// </summary>
internal sealed record class SerializableTypeModel(
    Accessibility Accessibility,
    TypeRef TypeSyntax,
    bool HasComplexBaseType,
    bool IncludePrimaryConstructorParameters,
    TypeRef BaseTypeSyntax,
    string Namespace,
    string GeneratedNamespace,
    string Name,
    bool IsValueType,
    bool IsSealedType,
    bool IsAbstractType,
    bool IsEnumType,
    bool IsGenericType,
    EquatableArray<TypeParameterModel> TypeParameters,
    EquatableArray<MemberModel> Members,
    bool UseActivator,
    bool IsEmptyConstructable,
    bool HasActivatorConstructor,
    bool TrackReferences,
    bool OmitDefaultMemberValues,
    EquatableArray<TypeRef> SerializationHooks,
    bool IsShallowCopyable,
    bool IsUnsealedImmutable,
    bool IsImmutable,
    bool IsExceptionType,
    EquatableArray<TypeRef> ActivatorConstructorParameters,
    ObjectCreationStrategy CreationStrategy,
    SourceLocationModel SourceLocation = default,
    TypeMetadataIdentity MetadataIdentity = default);
