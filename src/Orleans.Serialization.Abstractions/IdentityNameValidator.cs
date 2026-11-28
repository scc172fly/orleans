#nullable enable

using System;

namespace Orleans.Serialization;

// 与特性分析器共享，使声明在构建时与运行时遵循相同的契约。
internal static class IdentityNameValidator
{
    public const int MaxGenericArity = 64;

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return false;
        }

        var totalArity = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '"' or '\'' or '[' or ']' or '(' or ')' or ',' or '*' or '&'
                || char.IsWhiteSpace(character)
                || char.IsControl(character))
            {
                return false;
            }

            if (character == '`')
            {
                var arityStart = index + 1;
                var arity = 0;
                while (index + 1 < value.Length && value[index + 1] is >= '0' and <= '9')
                {
                    arity = arity * 10 + value[++index] - '0';
                    if (arity > MaxGenericArity)
                    {
                        return false;
                    }
                }

                // 运行时类型解析会读取泛型参数个数后缀，该后缀之后只能是嵌套类型或名称末尾。
                if (index < arityStart || index + 1 < value.Length && value[index + 1] != '+')
                {
                    return false;
                }

                totalArity += arity;
                if (totalArity > MaxGenericArity)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static void Validate(string value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        if (!IsValid(value))
        {
            throw new ArgumentException("An Orleans identity must be non-empty, contain no quotes, whitespace, control characters, or reserved type-name delimiters ([](),*&), and use decimal generic arity suffixes with a total arity of at most 64.", parameterName);
        }
    }
}
