using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Orleans.CodeGenerator;

/// <summary>
/// 描述可调用方法的代理基类，包括代理是 Grain 引用还是扩展，以及给定返回类型应使用哪些可调用对象基类型。
/// </summary>
internal sealed class InvokableMethodProxyBase(ProxyGenerationContext generationContext, InvokableMethodProxyBaseId descriptor, Dictionary<INamedTypeSymbol, INamedTypeSymbol> invokableBaseTypes) : IEquatable<InvokableMethodProxyBase>
{

    /// <summary>
    /// 获取代理生成上下文。
    /// </summary>
    public ProxyGenerationContext GenerationContext { get; } = generationContext;

    /// <summary>
    /// 获取代理基类 ID。
    /// </summary>
    public InvokableMethodProxyBaseId Key { get; } = descriptor;

    /// <summary>
    /// 获取代理基类型，例如 <c>GrainReference</c>。
    /// </summary>
    public INamedTypeSymbol ProxyBaseType => Key.ProxyBaseType;

    /// <summary>
    /// 获取一个值，指示此描述符是否表示扩展。
    /// </summary>
    public bool IsExtension => Key.IsExtension;

    /// <summary>
    /// 获取用于引用此代理基类的复合类型别名组成部分。
    /// </summary>
    public ImmutableArray<CompoundTypeAliasComponent> CompositeAliasComponents => Key.CompositeAliasComponents;

    /// <summary>
    /// 获取可调用对象基类型字典。它指示给定返回类型（例如 ValueTask）应使用哪个可调用对象基类型（例如 ValueTaskRequest）。
    /// </summary>
    public IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> InvokableBaseTypes { get; } = invokableBaseTypes ?? throw new ArgumentNullException(nameof(invokableBaseTypes));

    public bool Equals(InvokableMethodProxyBase other) => other is not null && Key.Equals(other.Key);
    public override bool Equals(object obj) => obj is InvokableMethodProxyBase other && Equals(other);
    public override int GetHashCode() => Key.GetHashCode();
    public override string ToString() => Key.ToString();
}
