using Orleans.CodeGenerator.SyntaxGeneration;
using Orleans.CodeGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Orleans.CodeGenerator;

internal class SerializableTypeDescription : ISerializableTypeDescription
{
    private readonly LibraryTypes _libraryTypes;

    public SerializableTypeDescription(Compilation compilation, INamedTypeSymbol type, bool supportsPrimaryConstructorParameters, IEnumerable<IMemberDescription> members, LibraryTypes libraryTypes)
    {
        Type = type;
        IncludePrimaryConstructorParameters = supportsPrimaryConstructorParameters;
        Members = [.. members];
        Compilation = compilation;
        _libraryTypes = libraryTypes;

        if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
        {
            // 辅助存取器可以进入私人存储,但外部代码必须能够标明其类型。
            foreach (var member in Members)
            {
                if ((member.IsSerializable || member.IsCopyable)
                    && !compilation.IsSymbolAccessibleWithin(member.Type, compilation.Assembly))
                {
                    throw new OrleansGeneratorDiagnosticAnalysisException(
                        InaccessibleSerializableTypeDiagnostic.CreateDiagnostic(member.Type, Location.None));
                }
            }
        }

        var t = type;
        Accessibility accessibility = t.DeclaredAccessibility;
        while (t is not null)
        {
            if ((int)t.DeclaredAccessibility < (int)accessibility)
            {
                accessibility = t.DeclaredAccessibility;
            }

            t = t.ContainingType;
        }

        Accessibility = accessibility;
        TypeParameters = new();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tp in type.GetAllTypeParameters())
        {
            var tpName = GetTypeParameterName(names, tp);
            TypeParameters.Add((tpName, tp));
        }

        SerializationHooks = new();
        if (type.GetAttributes(libraryTypes.SerializationCallbacksAttribute, out var hookAttributes))
        {
            foreach (var hookAttribute in hookAttributes)
            {
                var hookType = (INamedTypeSymbol)hookAttribute.ConstructorArguments[0].Value!;
                SerializationHooks.Add(hookType);
            }
        }

        ActivatorConstructorParameters = [];
        if (TryGetActivatorConstructor(type, _libraryTypes, out var constructorParameters))
        {
            HasActivatorConstructor = true;
            ActivatorConstructorParameters = constructorParameters;
        }

        static bool TryGetActivatorConstructor(INamedTypeSymbol type, LibraryTypes libraryTypes, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<TypeSyntax>? parameters)
        {
            parameters = null;
            if (type.IsAbstract)
            {
                return false;
            }

            foreach (var constructor in type.GetAllMembers<IMethodSymbol>())
            {
                if (constructor.MethodKind != MethodKind.Constructor || constructor.DeclaredAccessibility == Accessibility.Private || constructor.IsImplicitlyDeclared)
                {
                    continue;
                }

                if (constructor.HasAttribute(libraryTypes.GeneratedActivatorConstructorAttribute))
                {
                    foreach (var parameter in constructor.Parameters)
                    {
                        var argumentType = parameter.Type.ToTypeSyntax();
                        (parameters ??= new()).Add(argumentType);
                    }

                    break;
                }
            }

            return parameters is not null;
        }

        static string GetTypeParameterName(HashSet<string> names, ITypeParameterSymbol tp)
        {
            var count = 0;
            var result = tp.Name;
            while (names.Contains(result))
            {
                result = $"{tp.Name}_{++count}";
            }

            names.Add(result);
            return result.EscapeIdentifier();
        }
    }

    public INamedTypeSymbol Type { get; }

    public Accessibility Accessibility { get; }

    public TypeSyntax TypeSyntax => field ??= Type.ToTypeSyntax();

    public TypeSyntax BaseTypeSyntax => field ??= BaseType.ToTypeSyntax();

    public bool HasComplexBaseType => !IsValueType && BaseType is { SpecialType: not SpecialType.System_Object };

    public bool IncludePrimaryConstructorParameters { get; }

    public INamedTypeSymbol BaseType => field ??= GetEffectiveBaseType();

    private INamedTypeSymbol GetEffectiveBaseType()
    {
        var type = Type.EnumUnderlyingType ?? Type.BaseType;
        while (type != null && type.HasAttribute(_libraryTypes.SerializerTransparentAttribute))
            type = type.BaseType;
        return type!;
    }

    public string Namespace => Type.GetNamespaceAndNesting();

    public string GeneratedNamespace => Namespace switch
    {
        { Length: > 0 } ns => $"{GeneratedCodeUtilities.CodeGeneratorName}.{ns}",
        _ => GeneratedCodeUtilities.CodeGeneratorName
    };

    public string Name => Type.Name;

    public bool IsValueType => Type.IsValueType;
    public bool IsSealedType => Type.IsSealed;
    public bool IsAbstractType => Type.IsAbstract;
    public bool IsEnumType => Type.EnumUnderlyingType != null;

    public bool SupportsHotReloadMemberAddition => true;

    public bool IsGenericType => Type.IsGenericType;

    public List<(string Name, ITypeParameterSymbol Parameter)> TypeParameters { get; }

    public List<IMemberDescription> Members { get; }
    public Compilation Compilation { get; }
    public List<TypeSyntax> ActivatorConstructorParameters { get; }

    public bool IsEmptyConstructable
    {
        get
        {
            if (Type.Constructors.Length == 0)
            {
                return true;
            }

            // 要求成员的类型对于 Orleans来说是不可空建的,至少尚未。
            var t = Type;
            while (t != null)
            {
                foreach (var member in t.GetMembers())
                {
                    if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                    {
                        return false;
                    }
                }

                t = t.BaseType;
            }

            foreach (var ctor in Type.Constructors)
            {
                if (ctor.Parameters.Length != 0)
                {
                    continue;
                }

                switch (ctor.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                        return true;
                }
            }

            return false;
        }
    }

    public bool HasActivatorConstructor { get; }

    public bool UseActivator => Type.HasAttribute(_libraryTypes.UseActivatorAttribute) || !IsEmptyConstructable || HasActivatorConstructor;

    public bool TrackReferences => !IsValueType && !IsExceptionType && !Type.HasAttribute(_libraryTypes.SuppressReferenceTrackingAttribute);
    public bool OmitDefaultMemberValues => Type.HasAttribute(_libraryTypes.OmitDefaultMemberValuesAttribute);

    public List<INamedTypeSymbol> SerializationHooks { get; }

    public bool IsShallowCopyable => IsEnumType || !Type.HasBaseType(_libraryTypes.Exception) && _libraryTypes.IsShallowCopyable(Type);

    /// <summary>
    /// 获得这种工会类型的案例,或者如果不是工会类型的话 <see langword="null"/> 。
    /// </summary>
    public List<UnionCaseDescription>? UnionCases { get; init; }

    /// <summary>
    /// 获取这种封闭型的个案,或如果这种类型的非封闭型、其子类型声明案件识别符号的 <see langword="null"/> 。
    /// </summary>
    /// <remarks>
    /// 封闭型案件在子类型上申报,这些子类型可以放在其他源文件中,因此当生成源时就解决了。
    /// </remarks>
    public List<UnionCaseDescription>? GetClosedTypeCases() => UnionCaseResolver.GetClosedTypeCases(Type, Compilation, _libraryTypes);

    public bool IsUnsealedImmutable => !Type.IsSealed && IsImmutable;

    public bool IsImmutable => Type.HasAttribute(_libraryTypes.ImmutableAttribute);

    public bool IsExceptionType => Type.HasBaseType(_libraryTypes.Exception);

    public ExpressionSyntax GetObjectCreationExpression()
    {
        if (IsValueType)
        {
            return DefaultExpression(TypeSyntax);
        }

        var instanceConstructors = Type.InstanceConstructors;
        var isConstructible = false;
        if (!instanceConstructors.IsDefaultOrEmpty)
        {
            foreach (var ctor in instanceConstructors)
            {
                if (ctor.Parameters.IsDefaultOrEmpty)
                {
                    if (ctor.IsImplicitlyDeclared || ctor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                    {
                        isConstructible = true;
                    }

                    break;
                }
            }
        }

        if (isConstructible)
        {
            return ObjectCreationExpression(TypeSyntax, ArgumentList(), null);
        }
        else
        {
            return CastExpression(
                TypeSyntax,
                InvocationExpression(_libraryTypes.RuntimeHelpers.ToTypeSyntax().Member("GetUninitializedObject"))
                    .AddArgumentListArguments(
                        Argument(TypeOfExpression(TypeSyntax))));
        }
    }
}
