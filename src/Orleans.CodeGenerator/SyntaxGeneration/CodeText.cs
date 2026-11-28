using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.SyntaxGeneration;

/// <summary>
/// 为 <see cref="SourceWriter"/> 生成 C# 词法片段的代码文本。
/// </summary>
internal static class CodeText
{
    /// <summary>
    /// 按惯用空格格式生成类型、名称或类型实参片段：分隔已写元素的每个逗号之后添加空格，
    /// 例如 <c>Dictionary&lt;int, string&gt;</c>，但 <c>G&lt;,,&gt;</c> 和 <c>int[,]</c> 不加；相邻单词之间添加空格，
    /// 并在元组元素名称前添加空格（<c>(int[] items, int? count)</c>）。
    /// </summary>
    /// <remarks>
    /// 类型语法仅包含名称、关键字和标点，因此标记文本已足够：字面量或附加语法信息均不重要。
    /// </remarks>
    public static string Type(SyntaxNode type)
    {
        var builder = new StringBuilder();
        AppendType(builder, type);
        return builder.ToString();
    }

    public static void AppendType(StringBuilder builder, SyntaxNode type)
    {
        if (type.ContainsDiagnostics)
        {
            // 格式错误的代码文本（例如未转义的关键字命名空间，#11640）保留其原始文本，包括跳过的标记。
            builder.Append(type.ToFullString().Trim());
            return;
        }

        var previous = SyntaxKind.None;
        foreach (var token in type.DescendantTokens())
        {
            if (token.Span.IsEmpty)
            {
                // 省略的类型实参没有文本；后面的逗号或括号决定空格格式。
                continue;
            }

            var kind = token.Kind();
            if (previous == SyntaxKind.CommaToken && kind is not (SyntaxKind.CommaToken or SyntaxKind.GreaterThanToken or SyntaxKind.CloseBracketToken)
                || IsWord(previous) && IsWord(kind)
                || kind == SyntaxKind.IdentifierToken && token.Parent is TupleElementSyntax)
            {
                builder.Append(' ');
            }

            builder.Append(token.Text);
            previous = kind;
        }

        static bool IsWord(SyntaxKind kind) => kind == SyntaxKind.IdentifierToken || SyntaxFacts.IsKeywordKind(kind);
    }

    /// <summary>生成标识符的代码文本，像 <see cref="StringExtensions.ToIdentifier"/> 一样转义 C# 关键字及上下文关键字。</summary>
    public static string Identifier(string identifier)
    {
        identifier = identifier.TrimStart('@');
        return SyntaxGeneration.Identifier.IsCSharpKeyword(identifier) ? "@" + identifier : identifier;
    }

    /// <summary>为任意字符串值生成已转义的 C# 字符串字面量。</summary>
    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    /// 为文本加上引号但不进行转义。生成的程序集名称、别名和成员名称沿用此代码文本形式。
    /// </summary>
    public static string QuotedText(string value) => "\"" + value + "\"";

    public static string Literal(uint value) => value.ToString(CultureInfo.InvariantCulture) + "U";

    public static string Literal(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Literal(long value) => value.ToString(CultureInfo.InvariantCulture) + "L";

    /// <summary>为给定元数的开放泛型名称生成 <c>name&lt;,,&gt;</c> 的代码文本。</summary>
    public static string OpenGenericArguments(int arity) => arity switch
    {
        <= 0 => string.Empty,
        _ => "<" + new string(',', arity - 1) + ">",
    };
}
