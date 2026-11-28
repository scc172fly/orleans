using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.SyntaxGeneration;

/// <summary>
/// 为生成的私有字段生成名称，使成员重新排序和新增时名称仍保持稳定，
/// 以便 .NET 热重载识别为新增字段，而不是字段类型改变或重命名。
/// </summary>
internal static class GeneratedFieldNames
{
    private const int SecondaryHashSeed = unchecked((int)0x9E3779B9);

    public static string Accessor(string prefix, IMemberDescription member)
        => member.IsPrimaryConstructorParameter ? $"{prefix}_{member.FieldId}_ctor" : $"{prefix}_{member.FieldId}";

    public static string[] ForTypes(string prefix, IReadOnlyList<IMemberDescription> members)
    {
        var result = new string[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var key = TryGetTypeKey(member.Type) ?? "Type";
            var identity = $"{member.TypeName.Length}:{member.TypeName}{member.AssemblyName.Length}:{member.AssemblyName}";
            var hash = $"{GeneratedSourceOutput.CreateStableHash(identity)}{GeneratedSourceOutput.CreateStableHash(identity, SecondaryHashSeed)}";
            result[i] = $"{prefix}_{key}_{hash}";
        }

        return result;
    }

    private static string? TryGetTypeKey(ITypeSymbol type)
    {
        try
        {
            return Identifier.SanitizeIdentifierName(type.GetValidIdentifier());
        }
        catch (NotSupportedException)
        {
            // 某些类型（例如指针）没有有效的标识符形式，因此回退为仅使用哈希的形式。
            // 对于这些类型，GetValidIdentifier() 会抛出 NotSupportedException，因此捕获该异常并返回 null 来表示这种情况。
            return null;
        }
    }
}
