namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 手动注册的编解码器或提供程序的种类。
/// </summary>
internal enum RegisteredCodecKind : byte
{
    Serializer,
    Copier,
    Activator,
    Converter
}

/// <summary>
/// 描述使用 <c>[RegisterSerializer]</c>、<c>[RegisterCopier]</c>、
/// <c>[RegisterActivator]</c> 或 <c>[RegisterConverter]</c> 标注的类型。
/// </summary>
internal readonly record struct RegisteredCodecModel(
    TypeRef Type,
    RegisteredCodecKind Kind,
    EquatableArray<SerializationContractModel> Contracts = default);
