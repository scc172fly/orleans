using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Orleans.CodeGenerator;

/// <summary>
/// 提供与Roslyn财产、田地和建筑师参数符号有关的水电费。
/// </summary>
public static class PropertyUtility
{
    private static readonly Regex PropertyMatchRegex = new("^<([^>]+)>.*$", RegexOptions.Compiled);

    /// <summary>
    /// 获取由编译器生成的支持字段所代表属性。
    /// </summary>
    /// <param name="field">检查的字段。</param>
    /// <returns>完全匹配时对应的属性;否则, <see langword="null"/>。</returns>
    public static IPropertySymbol? GetMatchingProperty(IFieldSymbol field)
    {
        if (field.ContainingType is null)
            return null;
        return GetMatchingProperty(field, field.ContainingType.GetMembers());
    }

    /// <summary>
    /// 确定符号是否有编译器生成属性。
    /// </summary>
    /// <param name="symbol">检查的符号。</param>
    /// <returns><see langword="true"/> 何时 <paramref name="symbol"/> 具有编译器生成属性; 否则, <see langword="false"/>.</returns>
    public static bool IsCompilerGenerated(this ISymbol? symbol)
        => symbol?.GetAttributes().Any(a => a.AttributeClass?.Name == "CompilerGeneratedAttribute") == true;

    /// <summary>
    /// 确定属性的访问器是否是编译器生成的。
    /// </summary>
    /// <param name="property">检查的财产。</param>
    /// <returns><see langword="true"/> ,当两个存取器都是编译器生成时;否则, <see langword="false"/>。</returns>
    public static bool IsCompilerGenerated(this IPropertySymbol? property)
        => property?.GetMethod.IsCompilerGenerated() == true && property.SetMethod.IsCompilerGenerated();

    /// <summary>
    /// 获取代表属性的构造参数,与语义名称和类型匹配。
    /// </summary>
    /// <param name="property">匹配的属性。</param>
    /// <param name="constructorParameters">搜索的构建参数。</param>
    /// <returns>第一个匹配参数;否则, <see langword="null"/>。</returns>
    public static IParameterSymbol? GetMatchingPrimaryConstructorParameter(IPropertySymbol property, IEnumerable<IParameterSymbol> constructorParameters)
    {
        return constructorParameters.FirstOrDefault(p =>
            string.Equals(GetCanonicalName(p.Name), GetCanonicalName(property.Name), StringComparison.Ordinal) &&
            SymbolEqualityComparer.Default.Equals(p.Type, property.Type));
    }

    /// <summary>
    /// 从一组成员符号中获取由编译器生成的支持字段所代表属性。
    /// </summary>
    /// <param name="field">检查的字段。</param>
    /// <param name="memberSymbols">要搜索的成员符号。</param>
    /// <returns>完全匹配时对应的属性;否则, <see langword="null"/>。</returns>
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
    /// 获取存储财产价值的字段。
    /// </summary>
    /// <param name="property">检查的财产。</param>
    /// <returns>匹配字段,如果有一个完全匹配的字段;否则, <see langword="null"/>。</returns>
    public static IFieldSymbol? GetMatchingField(IPropertySymbol property)
    {
        if (property.ContainingType is null)
            return null;
        return GetMatchingField(property, property.ContainingType.GetMembers());
    }

    /// <summary>
    /// 从一组成员符号中获取存储属性价值的字段。
    /// </summary>
    /// <param name="property">检查的财产。</param>
    /// <param name="memberSymbols">要搜索的成员符号。</param>
    /// <returns>匹配字段,如果有一个完全匹配的字段;否则, <see langword="null"/>。</returns>
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
    /// 去除前加下划线并降低第一个字符,从而获得成员或参数名称的卡龙式形式。
    /// </summary>
    /// <param name="name">要加固的名 。</param>
    /// <returns>语句名称。</returns>
    public static string GetCanonicalName(string name)
    {
        name = name.TrimStart('_');
        if (name.Length > 0 && char.IsUpper(name[0]))
            name = $"{char.ToLowerInvariant(name[0])}{name.Substring(1)}";
        return name;
    }
}
