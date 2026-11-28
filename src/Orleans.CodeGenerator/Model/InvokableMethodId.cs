using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator;

/// <summary>
/// 标识一个可调用方法。
/// </summary>
internal readonly struct InvokableMethodId(InvokableMethodProxyBase proxyBaseInfo, INamedTypeSymbol interfaceType, IMethodSymbol method) : IEquatable<InvokableMethodId>
{
    /// <summary>
    /// 获取方法的代理基类信息（例如 GrainReference，以及它是否为扩展）。
    /// </summary>
    public InvokableMethodProxyBase ProxyBase { get; } = proxyBaseInfo;

    /// <summary>
    /// 获取方法符号。
    /// </summary>
    public IMethodSymbol Method { get; } = method;

    /// <summary>
    /// 获取包含该方法的接口符号。
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
