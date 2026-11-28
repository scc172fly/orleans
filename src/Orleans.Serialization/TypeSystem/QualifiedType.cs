
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 表示由程序集限定的类型。
    /// </summary>
    public readonly struct QualifiedType
    {
        /// <summary>
        /// 获取相等性比较器。
        /// </summary>
        /// <value>相等性比较器。</value>
        public static QualifiedTypeEqualityComparer EqualityComparer { get; } = new QualifiedTypeEqualityComparer();

        /// <summary>
        /// 初始化 <see cref="QualifiedType"/> 结构的新实例。
        /// </summary>
        /// <param name="assembly">程序集名称。</param>
        /// <param name="type">类型名称。</param>
        public QualifiedType(string? assembly, string type)
        {
            Assembly = assembly;
            Type = type;
        }

        /// <summary>
        /// 获取程序集名称。
        /// </summary>
        /// <value>程序集名称。</value>
        public string? Assembly { get; }

        /// <summary>
        /// 获取类型名称。
        /// </summary>
        /// <value>类型名称。</value>
        public string Type { get; }

        /// <summary>
        /// 解构此实例。
        /// </summary>
        /// <param name="assembly">程序集名称。</param>
        /// <param name="type">类型名称。</param>
        public void Deconstruct(out string? assembly, out string type)
        {
            assembly = Assembly;
            type = Type;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is QualifiedType type && string.Equals(Assembly, type.Assembly, StringComparison.Ordinal) && string.Equals(Type, type.Type, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Assembly, Type);

        /// <summary>
        /// 执行从 <see cref="System.ValueTuple{T1, T2}"/> 到 <see cref="QualifiedType"/> 的隐式转换。
        /// </summary>
        /// <param name="args">实参。</param>
        /// <returns>转换结果。</returns>
        public static implicit operator QualifiedType((string Assembly, string Type) args) => new(args.Assembly, args.Type);

        /// <summary>
        /// 比较两个值是否相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>运算符的结果。</returns>
        public static bool operator ==(QualifiedType left, QualifiedType right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// 比较两个值是否不相等。
        /// </summary>
        /// <param name="left">左操作数。</param>
        /// <param name="right">右操作数。</param>
        /// <returns>运算符的结果。</returns>
        public static bool operator !=(QualifiedType left, QualifiedType right)
        {
            return !(left == right);
        }

        /// <summary>
        /// <see cref="QualifiedType"/> 的相等性比较器。
        /// </summary>
        public sealed class QualifiedTypeEqualityComparer : IEqualityComparer<QualifiedType>
        {
            /// <inheritdoc/>
            public bool Equals(QualifiedType x, QualifiedType y) => x == y;

            /// <inheritdoc/>
            public int GetHashCode(QualifiedType obj) => obj.GetHashCode();
        }
    }
}
