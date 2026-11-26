using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 一种类型值类型的引用, 存储为字符串表示其语法 。
/// 用于增量管道模型,以避免持有 <c>ITypeSymbol</c> 参考文献。
/// </summary>
internal readonly record struct TypeRef
{
    public TypeRef(string syntaxString) => SyntaxString = syntaxString ?? string.Empty;

    public string SyntaxString { get; }

    /// <summary>
    /// 从存储的字符串重建 <see cref="TypeSyntax"/> 。
    /// </summary>
    public TypeSyntax ToTypeSyntax() => SyntaxFactory.ParseTypeName(SyntaxString);

    public override string ToString() => SyntaxString;

    public static TypeRef Empty { get; } = new TypeRef(string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(SyntaxString);
}
