using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.Diagnostics;
using Orleans.CodeGenerator.Model;
using Orleans.CodeGenerator.SyntaxGeneration;

namespace Orleans.CodeGenerator;

internal sealed class ProxyGenerationContext : IGeneratorServices
{
    private readonly Dictionary<INamedTypeSymbol, ProxyEmissionPlan> _proxyPlans = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, Dictionary<string, (string Namespace, InvokableEmissionPlan Plan)>> _compatibilityDeclarations = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<InvokableMethodId, InvokableMethodDescription> _invokableMethodDescriptions = new();
    private readonly Dictionary<INamedTypeSymbol, List<CompoundTypeAliasModel>> _compatibilityInvokableAliases = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _generatedCompatibilityInvokableMetadataNames = new(StringComparer.Ordinal);
    private readonly HashSet<INamedTypeSymbol> _visitedInterfaces = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, InvokableMethodProxyBase> _interfaceProxyBases = new(SymbolEqualityComparer.Default);

    internal ProxyGenerationContext(Compilation compilation, CodeGeneratorOptions options)
        : this(compilation, options, LibraryTypes.FromCompilation(compilation, options))
    {
    }

    internal ProxyGenerationContext(Compilation compilation, CodeGeneratorOptions options, LibraryTypes libraryTypes)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        LibraryTypes = libraryTypes ?? throw new ArgumentNullException(nameof(libraryTypes));
        InvokableBaseTypeResolver = new InvokableBaseTypeResolver(compilation);
        MetadataModel = new MetadataModel();
        ProxyGenerator = new ProxyGenerator(this, new CopierGenerator(this));
        InvokableGenerator = new InvokableGenerator(this);
    }

    public Compilation Compilation { get; }
    public CodeGeneratorOptions Options { get; }
    internal LibraryTypes LibraryTypes { get; }
    internal InvokableBaseTypeResolver InvokableBaseTypeResolver { get; }
    LibraryTypes IGeneratorServices.LibraryTypes => LibraryTypes;
    internal MetadataModel MetadataModel { get; }
    internal ProxyGenerator ProxyGenerator { get; }
    internal InvokableGenerator InvokableGenerator { get; }

    internal ProxyEmissionPlan GetProxyPlan(ProxyInterfaceDescription description)
        => _proxyPlans[description.InterfaceType.OriginalDefinition];

    internal IEnumerable<(string Namespace, InvokableEmissionPlan Plan)> GetCompatibilityDeclarations(INamedTypeSymbol interfaceType)
        => _compatibilityDeclarations.TryGetValue(interfaceType.OriginalDefinition, out var declarations) ? declarations.Values : [];

    internal IReadOnlyList<CompoundTypeAliasModel> GetCompatibilityInvokableAliases(INamedTypeSymbol interfaceType)
        => _compatibilityInvokableAliases.TryGetValue(interfaceType.OriginalDefinition, out var aliases) ? aliases : [];

    internal uint? GetId(ISymbol memberSymbol) => GeneratedCodeUtilities.GetId(LibraryTypes, memberSymbol);

    internal string? GetAlias(ISymbol symbol) => GeneratedCodeUtilities.GetAlias(LibraryTypes, symbol);

    internal void VisitInterface(INamedTypeSymbol interfaceType, INamedTypeSymbol proxyBaseType, bool isExtension)
    {
        // 获取或生成原始方法定义的可引用值。
        if (!SymbolEqualityComparer.Default.Equals(interfaceType, interfaceType.OriginalDefinition))
        {
            interfaceType = interfaceType.OriginalDefinition;
        }

        if (!_visitedInterfaces.Add(interfaceType))
        {
            return;
        }

        var proxyBase = GetProxyBaseDescription(proxyBaseType, isExtension);
        _interfaceProxyBases[interfaceType] = proxyBase;
        _ = GetInvokableInterfaceDescription(proxyBase.ProxyBaseType, interfaceType);
    }

    internal bool TryGetInvokableInterfaceDescription(INamedTypeSymbol interfaceType, [NotNullWhen(true)] out ProxyInterfaceDescription? result)
    {
        if (!TryGetProxyBaseDescription(interfaceType, out var description))
        {
            result = null;
            return false;
        }

        result = GetInvokableInterfaceDescription(description.ProxyBaseType, interfaceType);
        return true;
    }

    internal bool TryGetProxyBaseDescription(INamedTypeSymbol interfaceType, [NotNullWhen(true)] out InvokableMethodProxyBase? result)
    {
        if (_interfaceProxyBases.TryGetValue(interfaceType.OriginalDefinition, out result))
        {
            return true;
        }

        if (!InvokableBaseTypeResolver.TryGetProxyBaseType(
            interfaceType,
            LibraryTypes.GenerateMethodSerializersAttribute,
            out var proxyBaseType,
            out var isExtension))
        {
            result = null;
            return false;
        }

        result = GetProxyBaseDescription(proxyBaseType, isExtension);
        return true;
    }

    private InvokableMethodProxyBase GetProxyBaseDescription(INamedTypeSymbol proxyBaseType, bool isExtension)
    {
        proxyBaseType = proxyBaseType.OriginalDefinition;
        var invokableBaseTypes = GetInvokableBaseTypes(proxyBaseType);
        var descriptor = new InvokableMethodProxyBaseId(proxyBaseType, isExtension);
        var description = new InvokableMethodProxyBase(this, descriptor, invokableBaseTypes);
        return description;

        Dictionary<INamedTypeSymbol, INamedTypeSymbol> GetInvokableBaseTypes(INamedTypeSymbol baseClass)
        {
            // 设置在单个方法的属性不覆盖其特性时使用的可引用基本类型。
            if (!MetadataModel.ProxyBaseTypeInvokableBaseTypes.TryGetValue(baseClass, out var invokableBaseTypes))
            {
                invokableBaseTypes = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var mapping in InvokableBaseTypeResolver.GetMappingsForProxy(baseClass))
                {
                    invokableBaseTypes[mapping.ReturnType] = mapping.InvokableBaseType;
                }

                MetadataModel.ProxyBaseTypeInvokableBaseTypes[baseClass] = invokableBaseTypes;
            }

            return invokableBaseTypes;
        }
    }

    internal InvokableMethodProxyBase GetProxyBase(INamedTypeSymbol interfaceType)
    {
        if (!TryGetProxyBaseDescription(interfaceType, out var result))
        {
            throw new InvalidOperationException($"Cannot get proxy base description for a type which does not have or inherit [{nameof(LibraryTypes.GenerateMethodSerializersAttribute)}]");
        }

        return result;
    }

    private ProxyInterfaceDescription GetInvokableInterfaceDescription(INamedTypeSymbol proxyBaseType, INamedTypeSymbol interfaceType)
    {
        var originalInterface = interfaceType.OriginalDefinition;
        if (MetadataModel.InvokableInterfaces.TryGetValue(originalInterface, out var description))
        {
            return description;
        }

        description = new ProxyInterfaceDescription(this, proxyBaseType, originalInterface);
        MetadataModel.InvokableInterfaces.Add(originalInterface, description);

        // 代理计划发现其可引用的物;
        _proxyPlans.Add(originalInterface, ProxyGenerator.CreatePlan(description));

        return description;
    }

    internal ProxyMethodDescription GetProxyMethodDescription(INamedTypeSymbol interfaceType, IMethodSymbol method)
    {
        var originalMethod = method.OriginalDefinition;
        var proxyBaseInfo = GetProxyBase(interfaceType);
        if (!InvokableBaseTypeResolver.TryResolve(
            proxyBaseInfo.ProxyBaseType,
            method,
            interfaceType,
            out _,
            out var resolverDiagnostic)
            && resolverDiagnostic is { Kind: ResolverDiagnosticKind.InvalidMapping })
        {
            throw new OrleansGeneratorDiagnosticAnalysisException(
                InvokableBaseTypeMappingDiagnostic.CreateDiagnostic(resolverDiagnostic));
        }

        // 对于扩展名,我们希望确保包含型号始终是扩展名。
        // 这确保我们永远知道,在我们的 SetTarget 方法中,哪些“组件”是用来获取的。
        // 如果该类型不是扩展名,请使用原方法定义中包含类型的定义。
        // 这是最初定义类型时的界面 。
        var containingType = proxyBaseInfo.IsExtension ? interfaceType : originalMethod.ContainingType;

        var invokableId = new InvokableMethodId(proxyBaseInfo, containingType, originalMethod);
        var interfaceDescription = GetInvokableInterfaceDescription(invokableId.ProxyBase.ProxyBaseType, interfaceType);

        // 获取或生成原始方法定义的可引用值。
        if (!MetadataModel.GeneratedInvokables.TryGetValue(invokableId, out var generatedInvokable))
        {
            if (!_invokableMethodDescriptions.TryGetValue(invokableId, out var methodDescription))
            {
                methodDescription = _invokableMethodDescriptions[invokableId] = InvokableMethodDescription.Create(invokableId, containingType);
            }

            generatedInvokable = MetadataModel.GeneratedInvokables[invokableId] = InvokableGenerator.Generate(methodDescription);
        }

        var proxyMethodDescription = ProxyMethodDescription.Create(interfaceDescription, generatedInvokable, method);

        // 对于逆向兼容性,在具体执行类型不同时,也产生不同的援引者。
        if (Options.GenerateCompatibilityInvokers && !SymbolEqualityComparer.Default.Equals(method.OriginalDefinition.ContainingType, interfaceType))
        {
            var compatInvokableId = new InvokableMethodId(proxyBaseInfo, interfaceType, method);
            var compatMethodDescription = InvokableMethodDescription.Create(compatInvokableId, interfaceType);
            var includeGeneratedMethodId = !InvokableGenerator.IsGeneratedMethodIdExplicitlyClaimed(compatMethodDescription);
            var compatInvokable = InvokableGenerator.Generate(compatMethodDescription);
            if (!_compatibilityDeclarations.TryGetValue(interfaceType.OriginalDefinition, out var declarations))
            {
                declarations = _compatibilityDeclarations[interfaceType.OriginalDefinition] = new(StringComparer.Ordinal);
            }

            // 即使在另一个所有人要求使用相同的别名时,每个所有人仍保留其声明。
            if (!declarations.ContainsKey(compatInvokable.MetadataName))
            {
                declarations.Add(compatInvokable.MetadataName, (compatInvokable.GeneratedNamespace, compatInvokable.EmissionPlan));
            }

            if (!_generatedCompatibilityInvokableMetadataNames.Add(compatInvokable.MetadataName))
            {
                return proxyMethodDescription;
            }

            if (includeGeneratedMethodId)
            {
                var aliasComponents = InvokableGenerator.GetCompoundTypeAliasComponents(
                    compatInvokableId,
                    interfaceType,
                    compatMethodDescription.GeneratedMethodId);
                var alias = new CompoundTypeAliasModel(
                    aliasComponents.Select(static component => component.IsType
                        ? new CompoundAliasComponentModel(new TypeRef(component.TypeValue!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                        : new CompoundAliasComponentModel(component.StringValue!)).ToImmutableArray(),
                    new TypeRef(compatInvokable.OpenTypeSyntax.ToString()));
                if (!_compatibilityInvokableAliases.TryGetValue(interfaceType.OriginalDefinition, out var aliases))
                {
                    aliases = _compatibilityInvokableAliases[interfaceType.OriginalDefinition] = [];
                }

                aliases.Add(alias);
            }
        }

        return proxyMethodDescription;
    }
}
