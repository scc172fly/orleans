using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Orleans.CodeGenerator;

/// <summary>
/// 提供将 Roslyn 属性、字段和构造函数参数符号关联起来的实用工具。
/// </summary>
public static class PropertyUtility
{
    private static readonly Regex PropertyMatchRegex = new("^<([^>]+)>.*$", RegexOptions.Compiled);

    /// <summary>
    /// 获取编译器生成的后备字段所表示的属性。
    /// </summary>
    /// <param name="field">要检查的字段。</param>
    /// <returns>恰好有一个匹配项时返回匹配的属性，否则返回 <see langword="null"/>。</returns>
    public static IPropertySymbol? GetMatchingProperty(IFieldSymbol field)
    {
        if (field.ContainingType is null)
            return null;
        return GetMatchingProperty(field, field.ContainingType.GetMembers());
    }

    /// <summary>
    /// 确定符号是否具有编译器生成特性。
    /// </summary>
    /// <param name="symbol">要检查的符号。</param>
    /// <returns><see langword="true"/> 表示 <paramref name="symbol"/> 具有编译器生成特性；否则为 <see langword="false"/>。</returns>
    public static bool IsCompilerGenerated(this ISymbol? symbol)
        => symbol?.GetAttributes().Any(a => a.AttributeClass?.Name == "CompilerGeneratedAttribute") == true;

    /// <summary>
    /// 确定属性的访问器是否由编译器生成。
    /// </summary>
    /// <param name="property">要检查的属性。</param>
    /// <returns><see langword="true"/> 表示两个访问器均由编译器生成；否则为 <see langword="false"/>。</returns>
    public static bool IsCompilerGenerated(this IPropertySymbol? property)
        => property?.GetMethod.IsCompilerGenerated() == true && property.SetMethod.IsCompilerGenerated();

    /// <summary>
    /// 按规范名称及类型匹配，获取表示某属性的构造函数参数。
    /// </summary>
    /// <param name="property">要匹配的属性。</param>
    /// <param name="constructorParameters">要搜索的构造函数参数。</param>
    /// <returns>第一个匹配的参数；否则返回 <see langword="null"/>。</returns>
    public static IParameterSymbol? GetMatchingPrimaryConstructorParameter(IPropertySymbol property, IEnumerable<IParameterSymbol> constructorParameters)
    {
        return constructorParameters.FirstOrDefault(p =>
            string.Equals(GetCanonicalName(p.Name), GetCanonicalName(property.Name), StringComparison.Ordinal) &&
            SymbolEqualityComparer.Default.Equals(p.Type, property.Type));
    }

    /// <summary>
    /// 从一组成员符号中获取编译器生成的后备字段所表示的属性。
    /// </summary>
    /// <param name="field">要检查的字段。</param>
    /// <param name="memberSymbols">要搜索的成员符号。</param>
    /// <returns>恰好有一个匹配项时返回匹配的属性，否则返回 <see langword="null"/>。</returns>
    public static IPropertySymbol? GetMatchingProperty(IFieldSymbol field, IEnumerable<ISymbol> memberSymbols)
    {
        var propertyName = PropertyMatchRegex.Match(field.Name);
        if (!propertyName.Success)
        {
            return null;
        }

        var name = propertyName.Groups[1].Value;
        var candidates = memberSymbols.OfType<IPropertySymbol>()
            .Where(property => string.Equals(name, property.Name, StringComparison.Ordinal)
                               && SymbolEqualityComparer.Default.Equals(field.Type, property.Type)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// 获取存储属性值的字段。
    /// </summary>
    /// <param name="property">要检查的属性。</param>
    /// <returns>恰好有一个匹配项时返回匹配的字段，否则返回 <see langword="null"/>。</returns>
    public static IFieldSymbol? GetMatchingField(IPropertySymbol property)
    {
        if (property.ContainingType is null)
            return null;
        return GetMatchingField(property, property.ContainingType.GetMembers());
    }

    /// <summary>
    /// 从一组成员符号中获取存储属性值的字段。
    /// </summary>
    /// <param name="property">要检查的属性。</param>
    /// <param name="memberSymbols">要搜索的成员符号。</param>
    /// <returns>恰好有一个匹配项时返回匹配的字段，否则返回 <see langword="null"/>。</returns>
    public static IFieldSymbol? GetMatchingField(IPropertySymbol property, IEnumerable<ISymbol> memberSymbols)
    {
        var backingFieldName = $"<{property.Name}>k__BackingField";
        var candidates = (from field in memberSymbols.OfType<IFieldSymbol>()
                          where SymbolEqualityComparer.Default.Equals(field.Type, property.Type)
                          where field.Name == backingFieldName || GetCanonicalName(field.Name) == GetCanonicalName(property.Name)
                          select field).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// 通过移除前导下划线并将首字符转换为小写，获取成员或参数名称的规范形式。
    /// </summary>
    /// <param name="name">要规范化的名称。</param>
    /// <returns>规范名称。</returns>
    public static string GetCanonicalName(string name)
    {
        name = name.TrimStart('_');
        if (name.Length > 0 && char.IsUpper(name[0]))
            name = $"{char.ToLowerInvariant(name[0])}{name.Substring(1)}";
        return name;
    }
}
