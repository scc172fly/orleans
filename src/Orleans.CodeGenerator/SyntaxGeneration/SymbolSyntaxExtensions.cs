using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Orleans.CodeGenerator.SyntaxGeneration;

internal static class SymbolSyntaxExtensions
{
    /// <summary>
    /// 返回 System.String 代表当前 TypedConstant.
    /// </summary>
    /// <returns>A System.String 代表当前 TypedConstant.</returns>
    public static ExpressionSyntax ToExpression(this TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            throw new NotSupportedException($"Unsupported TypedConstant: {constant.ToCSharpString()}");
        }

        if (constant.Kind == TypedConstantKind.Type)
        {
            Debug.Assert(constant.Value is ITypeSymbol);
            return TypeOfExpression(((ITypeSymbol)constant.Value!).ToTypeSyntax());
        }

        if (constant.Kind == TypedConstantKind.Enum)
        {
            return DisplayEnumConstant(constant);
        }

        return ParseExpression(constant.ToCSharpString());
    }

    // 解码 enum 常值值值
    private static ExpressionSyntax DisplayEnumConstant(TypedConstant constant)
    {
        //typeName 字符串 = constant.Type.ToDisplayName();
        var constantToDecode = ConvertToUInt64(constant.Value);
        ulong curValue = 0;

        // 以所有常任成员在星号类型中进行循环
        var members = constant.Type!.GetMembers();
        var type = constant.Type.ToTypeSyntax();
        ExpressionSyntax? result = null;
        foreach (var member in members)
        {
            var field = member as IFieldSymbol;

            if (field is object && field.HasConstantValue)
            {
                ulong memberValue = ConvertToUInt64(field.ConstantValue);

                if (memberValue == constantToDecode)
                {
                    return constant.Type.ToTypeSyntax().Member(field.Name);
                }

                if ((memberValue & constantToDecode) == memberValue)
                {
                    // 更新当前值
                    curValue = curValue | memberValue;

                    var valueExpression = type.Member(field.Name);
                    if (result is null)
                    {
                        result = valueExpression;
                    }
                    else
                    {
                        result = BinaryExpression(SyntaxKind.BitwiseOrExpression, result, valueExpression);
                    }
                }
            }
        }

        Debug.Assert(result is not null);
        return result!;
    }

    private static ulong ConvertToUInt64(object? value)
    {
        return value switch
        {
            byte b => b,
            sbyte sb => (ulong)sb,
            short s => (ulong)s,
            ushort us => us,
            int i => (ulong)i,
            uint ui => ui,
            long l => (ulong)l,
            ulong ul => ul,
            _ => throw new NotSupportedException($"Type {value?.GetType()} not supported")
        };
    }
}
