namespace Orleans.CodeGenerator.Model;

// 每一碎片都拼写在目前的汇编上,然后才划出无符号缓存边界。
internal sealed record EnumSerializerPlan(
    string Name,
    bool IsPublic,
    string Type,
    string UnderlyingType,
    string StaticCodec,
    string CodecInterface,
    EmissionLibraryTypes Library);

// TypeSyntax 是可连号式的身份拼写,标明其源提示。
internal sealed record EnumSerializerOutputPlan(
    EnumSerializerPlan Serializer,
    string AssemblyName,
    TypeMetadataIdentity MetadataIdentity,
    string GeneratedNamespace,
    string TypeSyntax);
