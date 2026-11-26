using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.SyntaxGeneration;

/// <summary>
/// <see cref="SourceWriter"/>的拼写 C# 字元件。
/// </summary>
internal static class CodeText
{
    /// <summary>
    /// 拼写类型、名称或带有特殊间距的参数碎片类型、名称或类型:每个逗号后分隔的空格
    /// 字元(<c>Dictionary&lt;int, string&gt;</c>,但 <c>G&lt;,,&gt;</c> 和 <c>int[,]</c>,在相邻的单词之间,
    /// 和图普尔元素名称之前的(<c>(int[] items, int? count)</c>)。
    /// </summary>
    /// <remarks>
    /// type syntax 只保留姓名、关键字和标点, 所以象征性的文字就足够了: 没有字面或曲目是有意义的 。
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
            // 拼写错误, 如一个未隐藏的关键字名命名空间 (#11640) , 保留其文本, 包括跳过的标记 。
            builder.Append(type.ToFullString().Trim());
            return;
        }

        var previous = SyntaxKind.None;
        foreach (var token in type.DescendantTokens())
        {
            if (token.Span.IsEmpty)
            {
                // 寄出类型参数没有文字;以下逗号或括号决定间距。
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

    /// <summary>拼写识别符号,与 <see cref="StringExtensions.ToIdentifier"/> 一样,避开 C#关键字和背景关键词。</summary>
    public static string Identifier(string identifier)
    {
        identifier = identifier.TrimStart('@');
        return SyntaxGeneration.Identifier.IsCSharpKeyword(identifier) ? "@" + identifier : identifier;
    }

    /// <summary>拼写一个任意字符串值的逃逸 C# 字符串字典。</summary>
    public static string StringLiteral(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    /// 引号文本, 无需绕过它。 生成组装名称、 别名和成员姓名使用此已确立的拼写 。
    /// </summary>
    public static string QuotedText(string value) => "\"" + value + "\"";

    public static string Literal(uint value) => value.ToString(CultureInfo.InvariantCulture) + "U";

    public static string Literal(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Literal(long value) => value.ToString(CultureInfo.InvariantCulture) + "L";

    /// <summary>拼写 <c>name&lt;,,&gt;</c> 用于打开通用名称,且具有给定的正数。</summary>
    public static string OpenGenericArguments(int arity) => arity switch
    {
        <= 0 => string.Empty,
        _ => "<" + new string(',', arity - 1) + ">",
    };
}
