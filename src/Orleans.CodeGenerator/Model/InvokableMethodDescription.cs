using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.SyntaxGeneration;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.CodeGenerator;

/// <summary>
/// 描述一种可引用的方法。
/// 这是定义它的一个原始界面的方法 。
/// 相比之下, <see cref="ProxyMethodDescription"/> 则描述一个界面上的一种方法,该接口是生成替代工具时使用的,有类型的参数替代等。
/// </summary>
internal sealed class InvokableMethodDescription : IEquatable<InvokableMethodDescription>
{
    public static InvokableMethodDescription Create(InvokableMethodId method, INamedTypeSymbol containingType) => new(method, containingType);

    private InvokableMethodDescription(InvokableMethodId invokableId, INamedTypeSymbol containingType)
    {
        Key = invokableId;
        ContainingInterface = containingType;
        GeneratedMethodId = GeneratedCodeUtilities.CreateHashedMethodId(Method);
        MethodId = GeneratedCodeUtilities.GetMethodId(GenerationContext.LibraryTypes, Method) ?? GeneratedMethodId;
        ClaimedGeneratedMethodId = GeneratedCodeUtilities.GetClaimedGeneratedMethodId(
            GenerationContext.LibraryTypes,
            Method,
            containingType,
            ProxyBase.IsExtension);

        MethodTypeParameters = new List<(string Name, ITypeParameterSymbol Parameter)>();

        // 设置接口类型中的默认值。
        var invokableBaseTypes = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var pair in ProxyBase.InvokableBaseTypes)
        {
            invokableBaseTypes[pair.Key] = pair.Value;
        }

        InvokableBaseTypes = invokableBaseTypes;
        foreach (var methodAttr in Method.GetAttributes())
        {
            if (methodAttr.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            if (attributeClass.GetAttributes(GenerationContext.LibraryTypes.InvokableBaseTypeAttribute, out var attrs))
            {
                foreach (var attr in attrs)
                {
                    var ctorArgs = attr.ConstructorArguments;
                    var proxyBaseType = (INamedTypeSymbol)ctorArgs[0].Value!;
                    var returnType = (INamedTypeSymbol)ctorArgs[1].Value!;
                    var invokableBaseType = (INamedTypeSymbol)ctorArgs[2].Value!;
                    if (!SymbolEqualityComparer.Default.Equals(ProxyBase.ProxyBaseType, proxyBaseType))
                    {
                        // 此属性不适用于该特定援引者, 因为它是针对不同的代理基类型 。
                        continue;
                    }

                    invokableBaseTypes[returnType] = invokableBaseType;
                }
            }

            if (attributeClass.GetAttributes(GenerationContext.LibraryTypes.InvokableCustomInitializerAttribute, out attrs))
            {
                foreach (var attr in attrs)
                {
                    var methodName = (string)attr.ConstructorArguments[0].Value!;

                    TypedConstant methodArgument;
                    if (attr.ConstructorArguments.Length == 2)
                    {
                        // 直接从属性中取出值。
                        methodArgument = attr.ConstructorArguments[1];
                    }
                    else
                    {
                        // 从此属性所附属性中取值。
                        if (TryGetNamedArgument(attr.NamedArguments, "AttributeArgumentName", out var argNameArg)
                            && TryGetNamedArgument(methodAttr.NamedArguments, (string?)argNameArg.Value, out var namedArgument))
                        {
                            methodArgument = namedArgument;
                        }
                        else
                        {
                            var index = 0;
                            if (TryGetNamedArgument(attr.NamedArguments, "AttributeArgumentIndex", out var indexArg))
                            {
                                index = (int)indexArg.Value!;
                            }

                            methodArgument = methodAttr.ConstructorArguments[index];
                        }
                    }

                    CustomInitializerMethods.Add((methodName, methodArgument));
                }
            }

            if (SymbolEqualityComparer.Default.Equals(attributeClass, GenerationContext.LibraryTypes.ResponseTimeoutAttribute))
            {
                ResponseTimeoutTicks = TimeSpan.Parse((string)methodAttr.ConstructorArguments[0].Value!).Ticks;
            }
        }

        AllTypeParameters = new List<(string Name, ITypeParameterSymbol Parameter)>();
        MethodTypeParameters = new List<(string Name, ITypeParameterSymbol Parameter)>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeParameter in ContainingInterface.GetAllTypeParameters())
        {
            var tpName = GetTypeParameterName(names, typeParameter);
            AllTypeParameters.Add((tpName, typeParameter));
        }

        foreach (var typeParameter in Method.TypeParameters)
        {
            var tpName = GetTypeParameterName(names, typeParameter);
            AllTypeParameters.Add((tpName, typeParameter));
            MethodTypeParameters.Add((tpName, typeParameter));
        }

        TypeParameterSubstitutions = new(SymbolEqualityComparer.Default);
        foreach (var (name, parameter) in AllTypeParameters)
        {
            TypeParameterSubstitutions[parameter] = name;
        }

        static string GetTypeParameterName(HashSet<string> names, ITypeParameterSymbol typeParameter)
        {
            var count = 0;
            var result = typeParameter.Name;
            while (names.Contains(result))
            {
                result = $"{typeParameter.Name}_{++count}";
            }

            names.Add(result);
            return result.EscapeIdentifier();
        }

        static bool TryGetNamedArgument(ImmutableArray<KeyValuePair<string, TypedConstant>> arguments, string? name, out TypedConstant value)
        {
            foreach (var arg in arguments)
            {
                if (string.Equals(arg.Key, name, StringComparison.Ordinal))
                {
                    value = arg.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    /// 获取代理代用生成背景 。
    /// </summary>
    public ProxyGenerationContext GenerationContext => ProxyBase.GenerationContext;

    /// <summary>
    /// 获取方法标识符。
    /// </summary>
    public InvokableMethodId Key { get; }

    /// <summary>
    /// 获取该方法的代理服务器基信息(例如, GrainReference,是否为扩展名)。
    /// </summary>
    public InvokableMethodProxyBase ProxyBase => Key.ProxyBase;

    /// <summary>
    /// 获得方法符号。
    /// </summary>
    public IMethodSymbol Method => Key.Method;

    /// <summary>
    /// 获取可引用基型类型的词典。这表示何为可引用基类(例如, ValueTaskRequest)应该用于给定的返回类型(例如, ValueTask).
    /// </summary>
    public IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> InvokableBaseTypes { get; }

    /// <summary>
    /// 如果设置, 则会获得响应超时滴答。
    /// </summary>
    public long? ResponseTimeoutTicks { get; }

    /// <summary>
    /// 获取自定义初始化器方法名称及其相应参数列表。
    /// </summary>
    public List<(string MethodName, TypedConstant MethodArgument)> CustomInitializerMethods { get; } = new();

    /// <summary>
    /// 获取生成的方法标识符。
    /// </summary>
    public string GeneratedMethodId { get; }

    /// <summary>
    /// 获取方法标识符。
    /// </summary>
    public string MethodId { get; }

    /// <summary>
    /// 获取该方法的数值识别码所声称的生成的方法识别符号。
    /// </summary>
    public string? ClaimedGeneratedMethodId { get; }

    public List<(string Name, ITypeParameterSymbol Parameter)> AllTypeParameters { get; }
    public List<(string Name, ITypeParameterSymbol Parameter)> MethodTypeParameters { get; }
    public Dictionary<ITypeParameterSymbol, string> TypeParameterSubstitutions { get; }

    /// <summary>
    /// 获得一个值, 显示该方法是否有别名 。
    /// </summary>
    public bool HasAlias => !string.Equals(MethodId, GeneratedMethodId, StringComparison.Ordinal);

    /// <summary>
    /// 获取该类型所含的界面。
    /// </summary>
    public INamedTypeSymbol ContainingInterface { get; }

    /// <summary>
    /// 获得一个值,以显示该方法是否可被删除。
    /// </summary>
    public bool IsCancellable => Method.Parameters.Any(parameterSymbol => SymbolEqualityComparer.Default.Equals(GenerationContext.LibraryTypes.CancellationToken, parameterSymbol.Type));

    public bool Equals([DisallowNull] InvokableMethodDescription? other) => other is not null && Key.Equals(other.Key);
    public override bool Equals(object? obj) => obj is InvokableMethodDescription imd && Equals(imd);
    public override int GetHashCode() => Key.GetHashCode();
    public override string ToString() => $"{ProxyBase}/{ContainingInterface.Name}/{Method.Name}";
}
