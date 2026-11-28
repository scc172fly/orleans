using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.SyntaxGeneration;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.CodeGenerator;

/// <summary>
/// 描述可调用方法。
/// 这是最初定义该方法的原始接口上的方法。
/// 相比之下，<see cref="ProxyMethodDescription"/> 描述正在为其生成代理的接口上的方法，包括类型实参替换等信息。
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

        // 根据接口类型设置默认值。
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
                        // 此特性不适用于当前调用器，因为它针对不同的代理基类型。
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
                        // 直接从特性取得值。
                        methodArgument = attr.ConstructorArguments[1];
                    }
                    else
                    {
                        // 从附加了此特性的特性取得值。
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
    /// 获取代理生成上下文。
    /// </summary>
    public ProxyGenerationContext GenerationContext => ProxyBase.GenerationContext;

    /// <summary>
    /// 获取方法标识符。
    /// </summary>
    public InvokableMethodId Key { get; }

    /// <summary>
    /// 获取方法的代理基类信息（例如 GrainReference，以及它是否为扩展）。
    /// </summary>
    public InvokableMethodProxyBase ProxyBase => Key.ProxyBase;

    /// <summary>
    /// 获取方法符号。
    /// </summary>
    public IMethodSymbol Method => Key.Method;

    /// <summary>
    /// 获取可调用对象基类型字典。它指示给定返回类型（例如 ValueTask）应使用哪个可调用对象基类型（例如 ValueTaskRequest）。
    /// </summary>
    public IReadOnlyDictionary<INamedTypeSymbol, INamedTypeSymbol> InvokableBaseTypes { get; }

    /// <summary>
    /// 获取响应超时时间的计时单位数（ticks），如果已设置。
    /// </summary>
    public long? ResponseTimeoutTicks { get; }

    /// <summary>
    /// 获取自定义初始化器方法名称及其对应参数的列表。
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
    /// 获取此方法的数值标识符所占用的生成方法标识符（如果存在）。
    /// </summary>
    public string? ClaimedGeneratedMethodId { get; }

    public List<(string Name, ITypeParameterSymbol Parameter)> AllTypeParameters { get; }
    public List<(string Name, ITypeParameterSymbol Parameter)> MethodTypeParameters { get; }
    public Dictionary<ITypeParameterSymbol, string> TypeParameterSubstitutions { get; }

    /// <summary>
    /// 获取一个值，指示此方法是否具有别名。
    /// </summary>
    public bool HasAlias => !string.Equals(MethodId, GeneratedMethodId, StringComparison.Ordinal);

    /// <summary>
    /// 获取包含此类型的接口。
    /// </summary>
    public INamedTypeSymbol ContainingInterface { get; }

    /// <summary>
    /// 获取一个值，指示此方法是否可取消。
    /// </summary>
    public bool IsCancellable => Method.Parameters.Any(parameterSymbol => SymbolEqualityComparer.Default.Equals(GenerationContext.LibraryTypes.CancellationToken, parameterSymbol.Type));

    public bool Equals([DisallowNull] InvokableMethodDescription? other) => other is not null && Key.Equals(other.Key);
    public override bool Equals(object? obj) => obj is InvokableMethodDescription imd && Equals(imd);
    public override int GetHashCode() => Key.GetHashCode();
    public override string ToString() => $"{ProxyBase}/{ContainingInterface.Name}/{Method.Name}";
}
