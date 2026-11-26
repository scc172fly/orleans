using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.Diagnostics;
using Orleans.CodeGenerator.SyntaxGeneration;

namespace Orleans.CodeGenerator;

/// <summary>
/// 工会类型或封闭型等级的情况,在线上 <see cref="Id"/>标明。
/// </summary>
/// <param name="Id">案件识别符号,即联盟字段内案例值的实地识别符号。</param>
/// <param name="CaseType">案例类型,以工会类型参数表示。</param>
/// <param name="ValueType">案件值类型 : <paramref name="CaseType"/>,或其基本类型为无效值的类型。</param>
internal sealed record UnionCaseDescription(uint Id, ITypeSymbol CaseType, ITypeSymbol ValueType);

internal static class UnionCaseResolver
{
    /// <summary>
    /// 获得工会类型的情况按申报顺序排列,或 <see langword="null"/> 如果 <paramref name="type"/> 不属于工会类型。
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
    /// 获取按案件标识符或 <see langword="null"/> (如果 <paramref name="type"/> 不是封闭类型)订购的封闭型号
    /// 或如果其直接子类型都没有声明案件识别符号。
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
    /// 表示封闭型直接的子类型,即封闭型参数。
    /// </summary>
    /// <remarks>
    /// 通用封闭型案件必须是每种这类的即时推理,因此其基本类型的每一类辩论都必须是不同的
    /// 案件类型参数,如“圆环&lt;T&gt; :形状&lt;T&gt;' 。一个即时的子类型,例如`IntSquare :形状&lt;int&gt;' ,被否决。
    /// 测绘支持封闭型和宣布不属于通用含汞类型的案件。
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

        // 使用封闭类型类型参数为每个案件生成的封闭型名编码器,这些参数必须符合该案件的制约。
        if (!AreCaseConstraintsSatisfied(subtype, subtypeArguments, compilation))
        {
            throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateUnsupportedConstrainedCaseDiagnostic(subtype, closedType));
        }

        return subtype.Construct(subtypeArguments);
    }

    /// <summary>
    /// 如果封闭型号的类型参数被子类型参数所替代, 满足子类型的所有限制, 返回 <see langword="true"/> 。
    /// </summary>
    /// <remarks>
    /// 必须在相应的封闭型参数上宣布每项限制。仅含的制约,如通过等级约束执行的界面,将被否决。
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
        // 嵌套型的自有类型参数排除了含有类型的参数, 所以在构建嵌套型之前先用包含的类型替换 。
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
