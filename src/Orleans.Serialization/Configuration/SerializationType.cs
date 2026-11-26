using System;
using System.Collections.Generic;
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace Orleans.Serialization.Configuration;

/// <summary>
/// 描述使用具体类型和实施通用参数的序列型号。
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
    /// 描述具体类型或通用定义及其说明。
    /// </summary>
    /// <param name="type">具体类型或通用定义。</param>
    /// <param name="arguments">通用论点,按声明顺序排列。</param>
    /// <returns>类型描述。</returns>
    /// <remarks>
    /// 通用定义,无说明说明而提供的一般定义使用执行说明
    /// 目标匹配和可执行类型分辨率的宣布参数通用参数。
    /// 提供参数说明,以约束某一子参数或重新排序参数。
    /// 为此方法提供的数组类型必须关闭。 使用 <see cref="Array"/>
    /// 来描述结构目标匹配模式与通用元素参数的对比。
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
    /// 描述执行的通用参数。
    /// </summary>
    /// <param name="index">零基参数指数,包括宣布类型的参数。</param>
    /// <returns>参数说明。</returns>
    public static SerializationType Parameter(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new(null, index, 0, System.Array.Empty<SerializationType>());
    }

    /// <summary>
    /// 描述匹配序列化合同目标的阵列形状。
    /// </summary>
    /// <param name="element">元素类型描述。</param>
    /// <param name="rank">矩阵排序,从矢量的一开始。</param>
    /// <returns>数组说明。</returns>
    /// <remarks>
    /// 匹配所请求的目标类型时, 矩阵描述将元素参数绑定 。
    /// 可执行的分辨率类型需要使用源已知的封闭数组提供源名
    /// <see cref="Create"/> 或封闭式转换器注册。解析数组形状
    /// 是一个可执行文件类型, 在所有运行时投下 <see cref="NotSupportedException"/> 。
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
