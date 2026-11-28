using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.Diagnostics;
using Orleans.CodeGenerator.SyntaxGeneration;

namespace Orleans.CodeGenerator;

/// <summary>
/// 联合类型或封闭类型层次结构的分支，在传输格式中由 <see cref="Id"/> 标识。
/// </summary>
/// <param name="Id">分支标识符，即联合字段中分支值的字段标识符。</param>
/// <param name="CaseType">分支类型，以联合类型的类型参数表示。</param>
/// <param name="ValueType">分支值的类型：<paramref name="CaseType"/>，如果它是可空值类型，则为其底层类型。</param>
internal sealed record UnionCaseDescription(uint Id, ITypeSymbol CaseType, ITypeSymbol ValueType);

internal static class UnionCaseResolver
{
    /// <summary>
    /// 按声明顺序获取联合类型的分支，或返回 <see langword="null"/>（当 <paramref name="type"/> 不是联合类型时）。
    /// </summary>
    public static List<UnionCaseDescription>? GetUnionTypeCases(INamedTypeSymbol type, Compilation compilation, LibraryTypes libraryTypes)
    {
        if (!LanguageFeatureSymbols.IsUnion(type))
        {
            return null;
        }

        if (!type.IsValueType)
        {
            throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateClassUnionDiagnostic(type));
        }

        var caseTypes = LanguageFeatureSymbols.GetUnionCaseTypes(type);

        uint[] ids;
        if (libraryTypes.CaseIdsAttributeOrDefault is { } caseIdsAttributeType && type.GetAttribute(caseIdsAttributeType) is { } caseIdsAttribute)
        {
            var values = caseIdsAttribute.ConstructorArguments.FirstOrDefault().Values;
            ids = values.IsDefault ? [] : [.. values.Select(static value => (uint)value.Value!)];
            if (ids.Length != caseTypes.Length)
            {
                throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateCaseIdCountDiagnostic(type, ids.Length, caseTypes.Length));
            }
        }
        else
        {
            ids = [.. Enumerable.Range(0, caseTypes.Length).Select(static index => (uint)index)];
        }

        var result = new List<UnionCaseDescription>(caseTypes.Length);
        for (var i = 0; i < caseTypes.Length; i++)
        {
            var caseType = caseTypes[i].WithNullableAnnotation(NullableAnnotation.None);
            if (!compilation.IsSymbolAccessibleWithin(caseType, compilation.Assembly))
            {
                throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateInaccessibleCaseDiagnostic(type, caseType, type));
            }

            var valueType = caseType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0]
                : caseType;
            result.Add(new UnionCaseDescription(ids[i], caseType, valueType));
        }

        EnsureUniqueIds(type, type, result);
        return result;
    }

    /// <summary>
    /// 按分支标识符顺序获取封闭类型的分支，或返回 <see langword="null"/>（当 <paramref name="type"/> 不是封闭类型
    /// 或其直接子类型均未声明分支标识符时）。
    /// </summary>
    public static List<UnionCaseDescription>? GetClosedTypeCases(INamedTypeSymbol type, Compilation compilation, LibraryTypes libraryTypes)
    {
        if (!LanguageFeatureSymbols.IsClosed(type) || libraryTypes.CaseAttributeOrDefault is not { } caseAttributeType)
        {
            return null;
        }

        var definition = type.OriginalDefinition;
        var subtypes = definition.ContainingAssembly.GetDeclaredTypes()
            .Where(candidate => candidate.BaseType is { } baseType && SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, definition))
            .Select(subtype => (Subtype: subtype, Attribute: subtype.GetAttribute(caseAttributeType)))
            .ToList();
        if (!subtypes.Any(static entry => entry.Attribute is not null))
        {
            return null;
        }

        var result = new List<UnionCaseDescription>(subtypes.Count);
        foreach (var (subtype, attribute) in subtypes)
        {
            if (attribute is null)
            {
                throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateMissingCaseIdDiagnostic(subtype, definition));
            }

            if (!compilation.IsSymbolAccessibleWithin(subtype, compilation.Assembly))
            {
                throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateInaccessibleCaseDiagnostic(subtype, subtype, definition));
            }

            var id = (uint)attribute.ConstructorArguments[0].Value!;
            var caseType = GetClosedCaseType(subtype, definition, compilation);
            result.Add(new UnionCaseDescription(id, caseType, caseType));
        }

        EnsureUniqueIds(definition, null, result);
        result.Sort(static (x, y) => x.Id.CompareTo(y.Id));
        return result;
    }

    /// <summary>
    /// 以封闭类型的类型参数表示其直接子类型。
    /// </summary>
    /// <remarks>
    /// 泛型封闭类型的分支必须是该类型每种实例化的分支，因此其基类型的各类型实参必须是分支类型中不同的
    /// 类型参数，如 'Circle&lt;T&gt; : Shape&lt;T&gt;'。拒绝单个实例化的子类型，如 'IntSquare : Shape&lt;int&gt;'。
    /// 该映射支持未嵌套在泛型类型中的封闭类型及其分支。
    /// </remarks>
    private static INamedTypeSymbol GetClosedCaseType(INamedTypeSymbol subtype, INamedTypeSymbol closedType, Compilation compilation)
    {
        if (!closedType.IsGenericType && !subtype.IsGenericType)
        {
            return subtype;
        }

        var baseTypeArguments = subtype.BaseType!.TypeArguments;
        var subtypeArguments = new ITypeSymbol[subtype.TypeParameters.Length];
        var isFullyMapped = closedType.ContainingType is not { IsGenericType: true }
            && subtype.ContainingType is not { IsGenericType: true }
            && baseTypeArguments.Length == subtypeArguments.Length;
        for (var i = 0; isFullyMapped && i < baseTypeArguments.Length; i++)
        {
            if (baseTypeArguments[i] is ITypeParameterSymbol parameter
                && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, subtype)
                && subtypeArguments[parameter.Ordinal] is null)
            {
                subtypeArguments[parameter.Ordinal] = closedType.TypeParameters[i];
            }
            else
            {
                isFullyMapped = false;
            }
        }

        if (!isFullyMapped)
        {
            throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateUnsupportedGenericCaseDiagnostic(subtype, closedType));
        }

        // 封闭类型的生成编解码器使用封闭类型的类型参数引用每个分支的名称，因此这些参数必须满足分支的约束。
        if (!AreCaseConstraintsSatisfied(subtype, subtypeArguments, compilation))
        {
            throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateUnsupportedConstrainedCaseDiagnostic(subtype, closedType));
        }

        return subtype.Construct(subtypeArguments);
    }

    /// <summary>
    /// 如果用封闭类型的类型参数替换子类型的类型参数后，满足子类型的所有约束，则返回 <see langword="true"/>。
    /// </summary>
    /// <remarks>
    /// 每项约束都必须在对应的封闭类型参数上声明。拒绝仅隐含的约束，例如类约束所实现的接口。
    /// </remarks>
    private static bool AreCaseConstraintsSatisfied(INamedTypeSymbol subtype, ITypeSymbol[] subtypeArguments, Compilation compilation)
    {
        foreach (var parameter in subtype.TypeParameters)
        {
            var target = (ITypeParameterSymbol)subtypeArguments[parameter.Ordinal];
            var targetIsValueType = target.HasValueTypeConstraint || target.HasUnmanagedTypeConstraint;
            if (parameter.HasReferenceTypeConstraint && !target.HasReferenceTypeConstraint
                || parameter.HasValueTypeConstraint && !targetIsValueType
                || parameter.HasUnmanagedTypeConstraint && !target.HasUnmanagedTypeConstraint
                || parameter.HasConstructorConstraint && !target.HasConstructorConstraint && !targetIsValueType)
            {
                return false;
            }

            foreach (var constraintType in parameter.ConstraintTypes)
            {
                var substituted = Substitute(constraintType, subtype, subtypeArguments, compilation);
                if (!target.ConstraintTypes.Any(targetConstraint => SymbolEqualityComparer.Default.Equals(targetConstraint, substituted)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ITypeSymbol Substitute(ITypeSymbol type, INamedTypeSymbol subtype, ITypeSymbol[] subtypeArguments, Compilation compilation) => type switch
    {
        ITypeParameterSymbol parameter when SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, subtype) => subtypeArguments[parameter.Ordinal],
        IArrayTypeSymbol array => compilation.CreateArrayTypeSymbol(Substitute(array.ElementType, subtype, subtypeArguments, compilation), array.Rank),
        INamedTypeSymbol { IsGenericType: true } named => SubstituteNamedType(named, subtype, subtypeArguments, compilation),
        _ => type,
    };

    private static INamedTypeSymbol SubstituteNamedType(INamedTypeSymbol type, INamedTypeSymbol subtype, ITypeSymbol[] subtypeArguments, Compilation compilation)
    {
        // 嵌套类型自身的类型实参不包含所在类型的实参，因此在构造嵌套类型前先替换所在类型。
        var definition = type.ContainingType is { } containing
            ? ((INamedTypeSymbol)Substitute(containing, subtype, subtypeArguments, compilation)).GetTypeMembers(type.Name, type.Arity).Single()
            : type.OriginalDefinition;
        return type.Arity == 0
            ? definition
            : definition.Construct([.. type.TypeArguments.Select(argument => Substitute(argument, subtype, subtypeArguments, compilation))]);
    }

    private static void EnsureUniqueIds(INamedTypeSymbol unionType, ISymbol? location, List<UnionCaseDescription> cases)
    {
        var seen = new Dictionary<uint, UnionCaseDescription>();
        foreach (var unionCase in cases)
        {
            if (seen.TryGetValue(unionCase.Id, out var existing))
            {
                throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateDuplicateCaseIdDiagnostic(
                    location ?? unionCase.CaseType.OriginalDefinition,
                    unionCase.Id,
                    existing.CaseType,
                    unionCase.CaseType,
                    unionType));
            }

            seen.Add(unionCase.Id, unionCase);
        }
    }
}
