using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.SyntaxGeneration;

/// <summary>
/// 产生在成员重新排序和添加下稳定的私有字段的名称,
/// 以便 .NET 热重新装入能够看到添加而不是重排或重命名字段。
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
            // 有些类型 (e.g. 指针) 没有有效的标识表, 所以我们返回到只使用散列的表格 。
            // GetValidIdentifier() 扔掷 NotSupportedException 用来表示这些类型, 所以我们抓住它,然后返回无效。
            return null;
        }
    }
}
