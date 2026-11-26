#nullable enable

using System;

namespace Orleans.Serialization;

// 属性分析器共享,因此声明在构建时间和运行时间具有相同的合同。
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

                // 运行时间类型分析会消耗一个数后缀, 之后只有嵌套的类型或结尾。
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
