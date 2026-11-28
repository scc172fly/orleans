using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Orleans.CodeGenerator.Diagnostics;

namespace Orleans.CodeGenerator;

/// <summary>
/// 支持的编译器版本之间封闭类型及联合类型符号 API 的兼容性边界。
/// </summary>
/// <remarks>
/// 生成器针对旧版 Roslyn API 编译，以便能够在旧版编译器中加载。
/// 在这些功能出现之前的编译器无法声明封闭类型或联合类型，但可以引用由较新编译器编译的联合类型。
/// 这些编译器通过 <c>System.Runtime.CompilerServices.UnionAttribute</c> 元数据识别引用的联合类型，但不公开分支类型。
/// 声明联合类型但未公开其分支类型的编译器，在需要这些分支时会收到可据以采取措施的生成器诊断。
/// 此边界负责 API 发现、元数据识别及报告无法获取的分支类型。
/// 当最低支持的 Roslyn 版本公开全部三个属性时，将调用替换为
/// <c>ITypeSymbol.IsClosed</c>、<c>ITypeSymbol.IsUnion</c> 和 <c>ITypeSymbol.UnionCaseTypes</c>，然后删除此类
/// 及 <see cref="InvalidUnionCaseDiagnostic"/> 中无法获取分支类型的诊断。
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
    /// 按声明顺序获取联合类型的分支类型。
    /// </summary>
    /// <exception cref="OrleansGeneratorDiagnosticAnalysisException">
    /// 编译器不公开联合类型的分支类型。
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
