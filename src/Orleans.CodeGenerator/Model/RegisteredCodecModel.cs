namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 手动注册的编码/提供者类型
/// </summary>
internal enum RegisteredCodecKind : byte
{
    Serializer,
    Copier,
    Activator,
    Converter
}

/// <summary>
/// 描述注释类型 <c>[RegisterSerializer]</c>, <c>[RegisterCopier]</c>,
/// <c>[RegisterActivator]</c>]、或 <c>[RegisterConverter]</c>。
/// </summary>
internal readonly record struct RegisteredCodecModel(
    TypeRef Type,
    RegisteredCodecKind Kind,
    EquatableArray<SerializationContractModel> Contracts = default);
