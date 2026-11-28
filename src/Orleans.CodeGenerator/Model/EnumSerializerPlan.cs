namespace Orleans.CodeGenerator.Model;

// 在这个不含符号的缓存边界之前，依据当前编译生成每个片段的代码文本。
internal sealed record EnumSerializerPlan(
    string Name,
    bool IsPublic,
    string Type,
    string UnderlyingType,
    string StaticCodec,
    string CodecInterface,
    EmissionLibraryTypes Library);

// TypeSyntax 是可序列化类型标识的代码文本，用于命名其源文件提示名称。
internal sealed record EnumSerializerOutputPlan(
    EnumSerializerPlan Serializer,
    string AssemblyName,
    TypeMetadataIdentity MetadataIdentity,
    string GeneratedNamespace,
    string TypeSyntax);
