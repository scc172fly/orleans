using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 描述使用 <c>[GenerateSerializer]</c> 标注的类型，用于增量发现及元数据生成。
/// 生成序列化器、复制器和激活器时，依据当前编译解析其他语义依赖项。
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
