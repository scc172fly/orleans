using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Orleans.CodeGenerator;

/// <summary>
/// 描述可引用方法的代用基数,包括代理值是 grain 参考或扩展,以及对于特定返回类型应使用何种可引用基数类型。
/// </summary>
internal sealed class InvokableMethodProxyBase(ProxyGenerationContext generationContext, InvokableMethodProxyBaseId descriptor, Dictionary<INamedTypeSymbol, INamedTypeSymbol> invokableBaseTypes) : IEquatable<InvokableMethodProxyBase>
{

    /// <summary>
    /// 获取代理代用生成背景 。
    /// </summary>
    public ProxyGenerationContext GenerationContext { get; } = generationContext;

    /// <summary>
    /// 获取代理服务器的识别码 。
    /// </summary>
    public InvokableMethodProxyBaseId Key { get; } = descriptor;

    /// <summary>
    /// 获取代理服务器基类型,例如 <c>GrainReference</c>。
    /// </summary>
    public INamedTypeSymbol ProxyBaseType => Key.ProxyBaseType;

    /// <summary>
    /// 获得一个值,以显示该描述符是否代表扩展名。
    /// </summary>
    public bool IsExtension => Key.IsExtension;

    /// <summary>
    /// 获取用于指此代理基点的化合物型别名的组件。
    /// </summary>
    public ImmutableArray<CompoundTypeAliasComponent> CompositeAliasComponents => Key.CompositeAliasComponents;

    /// <summary>
    /// 获取可引用基型类型的词典。这表示何为可引用基类(例如, ValueTaskRequest)应该用于给定的返回类型(例如, ValueTask).
    /// </summary>
    public IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> InvokableBaseTypes { get; } = invokableBaseTypes ?? throw new ArgumentNullException(nameof(invokableBaseTypes));

    public bool Equals(InvokableMethodProxyBase other) => other is not null && Key.Equals(other.Key);
    public override bool Equals(object obj) => obj is InvokableMethodProxyBase other && Equals(other);
    public override int GetHashCode() => Key.GetHashCode();
    public override string ToString() => Key.ToString();
}
