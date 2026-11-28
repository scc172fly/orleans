using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.SyntaxGeneration;

/// <summary>
/// 对 <see cref="string"/> 类进行扩展以支持代码生成。
/// </summary>
internal static class StringExtensions
{
    public static SyntaxToken ToIdentifier(this string identifier)
    {
        identifier = identifier.TrimStart('@');
        if (Identifier.IsCSharpKeyword(identifier))
        {
            return SyntaxFactory.VerbatimIdentifier(
                SyntaxTriviaList.Empty,
                identifier,
                identifier,
                SyntaxTriviaList.Empty);
        }

        return SyntaxFactory.Identifier(SyntaxTriviaList.Empty, identifier, SyntaxTriviaList.Empty);
    }

    public static string EscapeIdentifier(this string str)
    {
        if (Identifier.IsCSharpKeyword(str))
        {
            return "@" + str;
        }

        return str;
    }

    public static IdentifierNameSyntax ToIdentifierName(this string identifier) => SyntaxFactory.IdentifierName(identifier.ToIdentifier());
}
