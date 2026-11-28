using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Orleans.CodeGenerator;

/// <summary>
/// 标识代理基类，包括代理是 Grain 引用还是扩展。
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
    /// 获取代理基类型，例如 <c>GrainReference</c>。
    /// </summary>
    public INamedTypeSymbol ProxyBaseType { get; }

    /// <summary>
    /// 获取一个值，指示此描述符是否表示扩展。
    /// </summary>
    public bool IsExtension { get; }

    /// <summary>
    /// 获取用于引用此代理基类的复合类型别名组成部分。
    /// </summary>
    public ImmutableArray<CompoundTypeAliasComponent> CompositeAliasComponents { get; }

    /// <summary>
    /// 获取用于在生成的类名称中区分此代理基类和其他代理基类的字符串。
    /// </summary>
    public string GeneratedClassNameComponent { get; }

    public bool Equals(InvokableMethodProxyBaseId other) => SymbolEqualityComparer.Default.Equals(ProxyBaseType, other.ProxyBaseType) && IsExtension == other.IsExtension;
    public override bool Equals(object obj) => obj is InvokableMethodProxyBaseId other && Equals(other);
    public override int GetHashCode() => IsExtension.GetHashCode() * 17 ^ SymbolEqualityComparer.Default.GetHashCode(ProxyBaseType);
    public override string ToString() => GeneratedClassNameComponent;
}
