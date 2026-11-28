using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Orleans.Serialization.TypeSystem;

/// <summary>
/// 用于格式化 <see cref="Type"/> 和 <see cref="TypeInfo"/> 实例的实用方法，格式化结果可由
/// <see cref="Type.GetType(string)"/> 解析。
/// </summary>
public static class RuntimeTypeNameFormatter
{
    private static readonly Assembly SystemAssembly = typeof(int).Assembly;

    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    /// <summary>
    /// 返回 <see cref="string"/> 形式的 <paramref name="type"/>，该形式可由 <see cref="Type.GetType(string)"/> 解析。
    /// </summary>
    /// <param name="type">要格式化的类型。</param>
    /// <returns>
    /// <see cref="string"/> 形式的 <paramref name="type"/>，该形式可由 <see cref="Type.GetType(string)"/> 解析。
    /// </returns>
    public static string Format(Type type)
    {
        if (type is null)
        {
            ThrowTypeArgumentNull();
        }

        return Cache.GetOrAdd(type, static t =>
        {
            var builder = new StringBuilder();
            Format(builder, t, isElementType: false);
            return builder.ToString();
        });
    }

    [DoesNotReturn]
    private static void ThrowTypeArgumentNull() => throw new ArgumentNullException("type");

    internal static string FormatInternalNoCache(Type type, bool allowAliases)
    {
        var builder = new StringBuilder();
        Format(builder, type, isElementType: false, allowAliases: allowAliases);
        return builder.ToString();
    }

    private static CompoundTypeAliasAttribute? GetCompoundTypeAlias(Type type)
    {
        var candidateValue = ulong.MaxValue;
        CompoundTypeAliasAttribute? candidate = null;
        foreach (var alias in type.GetCustomAttributes<CompoundTypeAliasAttribute>())
        {
            if (alias.Components[^1] is string str && ulong.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
            {
                // 对于数字别名，任选其中数值最小的别名。
                if (candidate is null || result < candidateValue)
                {
                    candidate = alias;
                    candidateValue = result;
                }
            }
            else
            {
                // 如果该值不是数字，则优先使用此别名，而不是任何数字别名。
                return alias;
            }
        }

        return candidate;
    }

    private static void Format(StringBuilder builder, Type type, bool isElementType, bool allowAliases = true)
    {
        if (allowAliases && GetCompoundTypeAlias(type) is { } compoundAlias)
        {
            AddCompoundTypeAlias(builder, type, compoundAlias.Components, allowAliases);
            AddGenericParameters(builder, type, allowAliases);
            return;
        }

        // 数组、指针和按引用传递的类型都是元素类型，需要按照各自的修饰符进行格式化。
        if (type.HasElementType)
        {
            // 格式化元素类型。
            Format(builder, type.GetElementType()!, isElementType: true, allowAliases: allowAliases);

            // 在元素类型上格式化此类型的修饰符。
            AddArrayRank(builder, type);
            AddPointerSymbol(builder, type);
            AddByRefSymbol(builder, type);
        }
        else
        {
            AddNamespace(builder, type);
            AddClassName(builder, type);
            AddGenericParameters(builder, type, allowAliases);
        }

        // 作为元素使用的类型在格式化时不包含其程序集名称，因为程序集名称将在
        // 元素类型的修饰符之后添加。
        if (!isElementType)
        {
            AddAssembly(builder, type);
        }
    }

    private static void AddCompoundTypeAlias(StringBuilder builder, Type type, object[] components, bool allowAliases)
    {
        // 开始。
        builder.Append('(');
        for (var i = 0; i < components.Length; ++i)
        {
            var component = components[i];
            if (i > 0)
            {
                builder.Append(',');
            }

            // 追加组成部分。
            if (component is string s)
            {
                builder.Append($"\"{s}\"");
            }
            else if (component is Type t)
            {
                if (t == type)
                {
                    throw new ArgumentException($"Element {i} of argument array, {component}, is equal to the attached type {type}, which is not supported");
                }

                builder.Append('[');
                Format(builder, t, isElementType: false, allowAliases: allowAliases);
                builder.Append(']');
            }
            else
            {
                throw new ArgumentException($"Element {i} of argument array, {component}, must be a Type or string but is an {component?.GetType().ToString() ?? "null"}");
            }
        }

        // 结束。
        builder.Append(')');
        if (type.IsGenericType)
        {
            int parameterCount = type.IsConstructedGenericType switch
            {
                true => type.GenericTypeArguments.Length,
                false => type.GetTypeInfo().GenericTypeParameters.Length
            };
            builder.Append($"`{parameterCount}");
        }
    }

    private static void AddNamespace(StringBuilder builder, Type type)
    {
        if (string.IsNullOrWhiteSpace(type.Namespace))
        {
            return;
        }

        _ = builder.Append(type.Namespace);
        _ = builder.Append('.');
    }

    private static void AddClassName(StringBuilder builder, Type type)
    {
        // 格式化声明类型。
        if (type.IsNested)
        {
            AddClassName(builder, type.DeclaringType!);
            _ = builder.Append('+');
        }

        // 格式化简单类型名称。
        var name = type.Name.AsSpan();
        var index = name.IndexOfAny("`*[&");
        _ = builder.Append(index > 0 ? name[..index] : name);

        // 格式化此类型的泛型参数个数。
        AddGenericArity(builder, type);
    }

    private static void AddGenericParameters(StringBuilder builder, Type type, bool allowAliases = true)
    {
        // 泛型类型定义（例如不带类型实参的 List<>）及非泛型类型，格式化结果中不包含
        // 任何类型实参。
        if (!type.IsConstructedGenericType)
        {
            return;
        }

        var args = type.GetGenericArguments();
        _ = builder.Append('[');
        for (var i = 0; i < args.Length; i++)
        {
            _ = builder.Append('[');
            Format(builder, args[i], isElementType: false, allowAliases: allowAliases);
            _ = builder.Append(']');
            if (i + 1 < args.Length)
            {
                _ = builder.Append(',');
            }
        }

        _ = builder.Append(']');
    }

    private static void AddGenericArity(StringBuilder builder, Type type)
    {
        if (!type.IsGenericType)
        {
            return;
        }

        // 泛型参数个数为泛型参数总数减去所有声明类型的泛型参数数目。
        var baseTypeParameterCount =
            type.IsNested ? type.DeclaringType!.GetGenericArguments().Length : 0;
        var arity = type.GetGenericArguments().Length - baseTypeParameterCount;

        // 如果所有泛型参数都位于声明类型中，则此类型没有自己的泛型参数。
        if (arity == 0)
        {
            return;
        }

        _ = builder.Append('`');
        _ = builder.Append(arity);
    }

    private static void AddPointerSymbol(StringBuilder builder, Type type)
    {
        if (!type.IsPointer)
        {
            return;
        }

        _ = builder.Append('*');
    }

    private static void AddByRefSymbol(StringBuilder builder, Type type)
    {
        if (!type.IsByRef)
        {
            return;
        }

        _ = builder.Append('&');
    }

    private static void AddArrayRank(StringBuilder builder, Type type)
    {
        if (!type.IsArray)
        {
            return;
        }

        _ = builder.Append('[');
        _ = builder.Append(',', type.GetArrayRank() - 1);
        _ = builder.Append(']');
    }

    private static void AddAssembly(StringBuilder builder, Type type)
    {
        // 不包含系统程序集的程序集名称。
        var assembly = type.Assembly;
        if (SystemAssembly.Equals(assembly))
        {
            return;
        }

        _ = builder.Append(',');
        _ = builder.Append(CachedTypeResolver.GetName(assembly));
    }
}
