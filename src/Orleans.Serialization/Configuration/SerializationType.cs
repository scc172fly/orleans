using System;
using System.Collections.Generic;
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace Orleans.Serialization.Configuration;

/// <summary>
/// 使用具体类型和实现的泛型参数描述序列化类型。
/// </summary>
public sealed class SerializationType
{
    private SerializationType(Type? type, int parameterIndex, int arrayRank, SerializationType[] arguments)
    {
        Type = type;
        ParameterIndex = parameterIndex;
        ArrayRank = arrayRank;
        Arguments = arguments;
    }

    internal Type? Type { get; }
    internal int ParameterIndex { get; }
    internal int ArrayRank { get; }
    internal SerializationType[] Arguments { get; }

    internal static bool AreEquivalent(SerializationType? left, SerializationType? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null || left.Type != right.Type || left.ParameterIndex != right.ParameterIndex
            || left.ArrayRank != right.ArrayRank || left.Arguments.Length != right.Arguments.Length)
        {
            return false;
        }
        for (var i = 0; i < left.Arguments.Length; i++)
        {
            if (!AreEquivalent(left.Arguments[i], right.Arguments[i]))
            {
                return false;
            }
        }
        return true;
    }

    internal static SerializationType FromType(Type type, Type[] implementationParameters)
    {
        if (type.IsGenericParameter)
        {
            var index = System.Array.IndexOf(implementationParameters, type);
            if (index < 0)
            {
                throw new InvalidOperationException($"Legacy serialization target parameter {type} is not declared by its implementation.");
            }
            return Parameter(index);
        }
        if (type.IsArray && type.ContainsGenericParameters)
        {
            return Array(FromType(type.GetElementType()!, implementationParameters), type.GetArrayRank());
        }
        if (type.IsGenericType && type.ContainsGenericParameters)
        {
            var arguments = type.GetGenericArguments();
            var descriptions = new SerializationType[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                descriptions[i] = FromType(arguments[i], implementationParameters);
            }
            return new(type.GetGenericTypeDefinition(), -1, 0, descriptions);
        }
        return new(type, -1, 0, System.Array.Empty<SerializationType>());
    }

    /// <summary>
    /// 描述具体类型或泛型类型定义及其实参描述。
    /// </summary>
    /// <param name="type">具体类型或泛型定义。</param>
    /// <param name="arguments">按声明顺序排列的泛型实参。</param>
    /// <returns>类型描述。</returns>
    /// <remarks>
    /// 如果提供泛型定义时未提供实参描述，则使用该实现按声明顺序排列的
    /// 泛型参数来匹配目标并解析可执行类型。
    /// 提供实参描述可绑定部分形参或调整形参顺序。
    /// 传给此方法的数组类型必须为封闭类型。使用 <see cref="Array"/>
    /// 描述包含泛型元素参数的结构化目标匹配模式。
    /// </remarks>
    public static SerializationType Create(
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
#endif
        Type type,
        params SerializationType[] arguments)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        if (type.IsArray && type.ContainsGenericParameters)
        {
            throw new ArgumentException(
                "An executable array type must be closed. Use SerializationType.Array to describe a structural target-matching pattern.",
                nameof(type));
        }

        if (arguments.Length > 0 && (!type.IsGenericTypeDefinition || type.GetGenericArguments().Length != arguments.Length))
        {
            throw new ArgumentException("The generic argument descriptions must match the type definition's arity.", nameof(arguments));
        }

        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("Generic argument descriptions must be non-null.", nameof(arguments));
            }
        }

        return new(type, -1, 0, (SerializationType[])arguments.Clone());
    }

    /// <summary>
    /// 描述实现的一个泛型参数。
    /// </summary>
    /// <param name="index">从零开始的参数索引，包括声明类型的参数。</param>
    /// <returns>参数描述。</returns>
    public static SerializationType Parameter(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new(null, index, 0, System.Array.Empty<SerializationType>());
    }

    /// <summary>
    /// 描述用于匹配序列化契约目标的数组形状。
    /// </summary>
    /// <param name="element">元素类型描述。</param>
    /// <param name="rank">数组的秩，从向量的秩 1 开始。</param>
    /// <returns>数组描述。</returns>
    /// <remarks>
    /// 匹配请求的目标类型时，数组描述会绑定元素参数。
    /// 解析可执行类型需要使用以下方式提供源代码中已知的封闭数组类型：
    /// <see cref="Create"/> 或封闭转换器注册。将数组形状
    /// 解析为可执行类型，在所有运行时上都会抛出 <see cref="NotSupportedException"/>。
    /// </remarks>
    public static SerializationType Array(SerializationType element, int rank = 1)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (rank < 1 || rank > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(rank));
        }

        return new(null, -1, rank, [element]);
    }

    internal IEnumerable<Type> GetReferencedTypes()
    {
        if (Type is { } type)
        {
            yield return type;
        }

        foreach (var argument in Arguments)
        {
            foreach (var referenced in argument.GetReferencedTypes())
            {
                yield return referenced;
            }
        }
    }
}
