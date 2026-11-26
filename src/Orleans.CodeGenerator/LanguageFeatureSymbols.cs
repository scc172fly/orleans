using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.Diagnostics;

namespace Orleans.CodeGenerator;

/// <summary>
/// 封闭型和工会标志API在所支持的汇编者版本之间的兼容性边界。
/// </summary>
/// <remarks>
/// 发电机是用老Roslyn API 编译的,这样它就装在旧的编译器上。
/// 在上述特征之前的汇编者不能宣布封闭或工会类型,但可以参考较新的汇编者所编制的工会类型。
/// 以其 <c>System.Runtime.CompilerServices.UnionAttribute</c> 并披露任何案件类型。
/// 申报工会但没有发现任何案件类型的汇编者,在需要这类案件时,可得到可诉的发电机诊断。
/// 该边界拥有 API 发现、元数据识别和报告无法提供的个案类型。
/// 一旦最小支持的Roslyn 版本版本显示所有三种属性, 用以下方式替换电话
/// <c>ITypeSymbol.IsClosed</c>, <c>ITypeSymbol.IsUnion</c>,和 <c>ITypeSymbol.UnionCaseTypes</c>,然后删除此类
/// 以及从 <see cref="InvalidUnionCaseDiagnostic"/>获得的无病例型诊断。
/// </remarks>
internal static class LanguageFeatureSymbols
{
    private static readonly Func<ITypeSymbol, bool>? IsClosedAccessor = CreateAccessor<bool>("IsClosed");
    private static readonly Func<ITypeSymbol, bool>? IsUnionAccessor = CreateAccessor<bool>("IsUnion");
    private static readonly Func<ITypeSymbol, ImmutableArray<ITypeSymbol>>? UnionCaseTypesAccessor = CreateAccessor<ImmutableArray<ITypeSymbol>>("UnionCaseTypes");

    public static bool IsClosed(ITypeSymbol type) => IsClosedAccessor?.Invoke(type) == true;

    public static bool IsUnion(ITypeSymbol type) => IsUnionAccessor is { } isUnion ? isUnion(type) : HasUnionAttribute(type);

    private static bool HasUnionAttribute(ITypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Struct or TypeKind.Class))
        {
            return false;
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is
                {
                    Name: "UnionAttribute",
                    ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } }
                })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获得工会类型类型的案件按申报顺序排列。
    /// </summary>
    /// <exception cref="OrleansGeneratorDiagnosticAnalysisException">
    /// 汇编者并不披露工会案件的类型。
    /// </exception>
    public static ImmutableArray<ITypeSymbol> GetUnionCaseTypes(INamedTypeSymbol type)
    {
        if (UnionCaseTypesAccessor is { } accessor)
        {
            return accessor(type);
        }

        throw new OrleansGeneratorDiagnosticAnalysisException(InvalidUnionCaseDiagnostic.CreateUnionCaseTypesUnavailableDiagnostic(type));
    }

    private static Func<ITypeSymbol, T>? CreateAccessor<T>(string propertyName)
    {
        var property = typeof(ITypeSymbol).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetMethod is not { } getter || property.PropertyType != typeof(T))
        {
            return null;
        }

        return (Func<ITypeSymbol, T>)Delegate.CreateDelegate(typeof(Func<ITypeSymbol, T>), getter);
    }
}
