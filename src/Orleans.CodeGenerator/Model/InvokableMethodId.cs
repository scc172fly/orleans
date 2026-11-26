using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator;

/// <summary>
/// 确定一种可援引的方法。
/// </summary>
internal readonly struct InvokableMethodId(InvokableMethodProxyBase proxyBaseInfo, INamedTypeSymbol interfaceType, IMethodSymbol method) : IEquatable<InvokableMethodId>
{
    /// <summary>
    /// 获取该方法的代理服务器基信息(例如, GrainReference,是否为扩展名)。
    /// </summary>
    public InvokableMethodProxyBase ProxyBase { get; } = proxyBaseInfo;

    /// <summary>
    /// 获得方法符号。
    /// </summary>
    public IMethodSymbol Method { get; } = method;

    /// <summary>
    /// 获得包含界面符号。
    /// </summary>
    public INamedTypeSymbol InterfaceType { get; } = interfaceType;

    public bool Equals(InvokableMethodId other) =>
        ProxyBase.Equals(other.ProxyBase)
        && SymbolEqualityComparer.Default.Equals(Method, other.Method)
        && SymbolEqualityComparer.Default.Equals(InterfaceType, other.InterfaceType);

    public override bool Equals(object obj) => obj is InvokableMethodId imd && Equals(imd);
    public override int GetHashCode()
    {
        unchecked
        {
            return ProxyBase.GetHashCode()
                * 17 ^ SymbolEqualityComparer.Default.GetHashCode(Method)
                * 17 ^ SymbolEqualityComparer.Default.GetHashCode(InterfaceType);
        }
    }

    public override string ToString() => $"{ProxyBase}/{InterfaceType.Name}/{Method.Name}";
}
