using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Orleans.CodeGenerator;

/// <summary>
/// 指定代理服务器基础,包括代理服务器是 grain 参考还是扩展。
/// </summary>
internal readonly struct InvokableMethodProxyBaseId : IEquatable<InvokableMethodProxyBaseId>
{
    public InvokableMethodProxyBaseId(INamedTypeSymbol type, bool isExtension)
    {
        if (!SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition))
        {
            throw new ArgumentException("Type must be an original definition. This is a code generator bug.");
        }

        ProxyBaseType = type;
        IsExtension = isExtension;

        if (IsExtension)
        {
            CompositeAliasComponents = [new(ProxyBaseType), new("Ext")];
            GeneratedClassNameComponent = $"{ProxyBaseType.Name}_Ext";
        }
        else
        {
            CompositeAliasComponents = [new(ProxyBaseType)];
            GeneratedClassNameComponent = ProxyBaseType.Name;
        }
    }

    /// <summary>
    /// 获取代理服务器基类型,例如 <c>GrainReference</c>。
    /// </summary>
    public INamedTypeSymbol ProxyBaseType { get; }

    /// <summary>
    /// 获得一个值,以显示该描述符是否代表扩展名。
    /// </summary>
    public bool IsExtension { get; }

    /// <summary>
    /// 获取用于指此代理基点的化合物型别名的组件。
    /// </summary>
    public ImmutableArray<CompoundTypeAliasComponent> CompositeAliasComponents { get; }

    /// <summary>
    /// 获取用于在生成类名中区分此代理基数和其它基数的字符串。
    /// </summary>
    public string GeneratedClassNameComponent { get; }

    public bool Equals(InvokableMethodProxyBaseId other) => SymbolEqualityComparer.Default.Equals(ProxyBaseType, other.ProxyBaseType) && IsExtension == other.IsExtension;
    public override bool Equals(object obj) => obj is InvokableMethodProxyBaseId other && Equals(other);
    public override int GetHashCode() => IsExtension.GetHashCode() * 17 ^ SymbolEqualityComparer.Default.GetHashCode(ProxyBaseType);
    public override string ToString() => GeneratedClassNameComponent;
}
