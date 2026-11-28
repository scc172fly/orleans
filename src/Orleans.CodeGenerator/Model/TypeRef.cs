using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 对类型的值类型引用，以其语法的字符串表示形式保存。
/// 用于增量管道模型，避免持有 <c>ITypeSymbol</c> 引用。
/// </summary>
internal readonly record struct TypeRef
{
    public TypeRef(string syntaxString) => SyntaxString = syntaxString ?? string.Empty;

    public string SyntaxString { get; }

    /// <summary>
    /// 从已保存的字符串重新构造 <see cref="TypeSyntax"/>。
    /// </summary>
    public TypeSyntax ToTypeSyntax() => SyntaxFactory.ParseTypeName(SyntaxString);

    public override string ToString() => SyntaxString;

    public static TypeRef Empty { get; } = new TypeRef(string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(SyntaxString);
}
