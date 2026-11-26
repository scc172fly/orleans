using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Orleans.Serialization.TypeSystem;

/// <summary>
/// 格式化 <see cref="Type"/> 和 <see cref="TypeInfo"/> 实例的实用方法,其格式化方式可以由
/// <see cref="Type.GetType(string)"/>.
/// </summary>
public static class RuntimeTypeNameFormatter
{
    private static readonly Assembly SystemAssembly = typeof(int).Assembly;

    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    /// <summary>
    /// 返回 <see cref="string"/> 形式的 <paramref name="type"/> ,该 <see cref="Type.GetType(string)"/>可以进行解析。
    /// </summary>
    /// <param name="type">格式的类型。</param>
    /// <returns>
    /// 一种 <see cref="string"/> 形式 <paramref name="type"/> ,可以由 <see cref="Type.GetType(string)"/>加以分析。
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
                // 对于数字化的别名, 任意选择最低值的
                if (candidate is null || result < candidateValue)
                {
                    candidate = alias;
                    candidateValue = result;
                }
            }
            else
            {
                // 如果值不是数字,则更倾向于使用这个别名而不是任何数字。
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

        // 阵列、指针和副反射类型都是元素类型,需要以其自身装饰的形式格式化。
        if (type.HasElementType)
        {
            // 格式化元素类型。
            Format(builder, type.GetElementType()!, isElementType: true, allowAliases: allowAliases);

            // 格式化此类型对元素类型的装饰。
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

        // 用作元素的类型不使用组装名称格式化,因为此类型添加在
        // 元素类型装饰。
        if (!isElementType)
        {
            AddAssembly(builder, type);
        }
    }

    private static void AddCompoundTypeAlias(StringBuilder builder, Type type, object[] components, bool allowAliases)
    {
        // 开始
        builder.Append('(');
        for (var i = 0; i < components.Length; ++i)
        {
            var component = components[i];
            if (i > 0)
            {
                builder.Append(',');
            }

            // 附加组件
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

        // 结束
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

        // 格式化此类型的一般正数 。
        AddGenericArity(builder, type);
    }

    private static void AddGenericParameters(StringBuilder builder, Type type, bool allowAliases = true)
    {
        // 通用类型定义(例如,清单* *}没有参数)和非基因类型不包括任何
        // 格式化参数。
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

        // 均匀性是指在申报类型中通用参数数减去通用参数数。
        var baseTypeParameterCount =
            type.IsNested ? type.DeclaringType!.GetGenericArguments().Length : 0;
        var arity = type.GetGenericArguments().Length - baseTypeParameterCount;

        // 如果所有通用参数都属于申报类型,则这种类型的参数本身没有。
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
        // 不包括系统组装的组装名称。
        var assembly = type.Assembly;
        if (SystemAssembly.Equals(assembly))
        {
            return;
        }

        _ = builder.Append(',');
        _ = builder.Append(CachedTypeResolver.GetName(assembly));
    }
}
