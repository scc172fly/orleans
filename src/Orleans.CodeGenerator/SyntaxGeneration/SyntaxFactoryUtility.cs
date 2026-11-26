using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Orleans.CodeGenerator.SyntaxGeneration;

internal static class SyntaxFactoryUtility
{
    /// <summary>
    /// 返回成员访问语法。
    /// </summary>
    /// <param name="instance">
    /// 举个例子
    /// </param>
    /// <param name="member">
    /// 成员。
    /// </param>
    /// <returns>
    /// 结果的 <see cref="MemberAccessExpressionSyntax"/>。
    /// </returns>
    public static MemberAccessExpressionSyntax Member(this ExpressionSyntax instance, string member) => instance.Member(member.ToIdentifierName());

    /// <summary>
    /// 返回成员访问语法。
    /// </summary>
    /// <param name="instance">
    /// 举个例子
    /// </param>
    /// <param name="member">
    /// 成员。
    /// </param>
    /// <returns>
    /// 结果的 <see cref="MemberAccessExpressionSyntax"/>。
    /// </returns>
    public static MemberAccessExpressionSyntax Member(this ExpressionSyntax instance, IdentifierNameSyntax member) => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, instance, member);

    /// <summary>
    /// 返回每个类型参数的拼写限制,按声明顺序排列,并将类型参数重新命名为生成的名称。
    /// </summary>
    public static List<(string Name, List<string> Constraints)> GetTypeParameterConstraintTexts(List<(string Name, ITypeParameterSymbol Parameter)> typeParameters)
    {
        var allConstraints = new List<(string, List<string>)>(typeParameters.Count);
        var substitutions = new Dictionary<ITypeParameterSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var (name, parameter) in typeParameters)
        {
            substitutions[parameter] = name;
        }

        foreach (var (name, tp) in typeParameters)
        {
            var constraints = new List<string>();
            if (tp.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (tp.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (tp.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }
            else if (tp.HasReferenceTypeConstraint)
            {
                constraints.Add("class");
            }

            foreach (var c in tp.ConstraintTypes)
            {
                constraints.Add(CodeText.Type(c.ToTypeSyntax(substitutions)));
            }

            if (tp.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            allConstraints.Add((name, constraints));
        }

        return allConstraints;
    }
}
