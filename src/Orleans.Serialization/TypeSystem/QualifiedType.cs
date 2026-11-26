
using System;
using System.Collections.Generic;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 表示组装-数量型号。
    /// </summary>
    public readonly struct QualifiedType
    {
        /// <summary>
        /// 获得平等比较器。
        /// </summary>
        /// <value>平等比较器。</value>
        public static QualifiedTypeEqualityComparer EqualityComparer { get; } = new QualifiedTypeEqualityComparer();

        /// <summary>
        /// 初始化 <see cref="QualifiedType"/> 结构的新实例。
        /// </summary>
        /// <param name="assembly">组装。</param>
        /// <param name="type">类型。</param>
        public QualifiedType(string? assembly, string type)
        {
            Assembly = assembly;
            Type = type;
        }

        /// <summary>
        /// 获得集会。
        /// </summary>
        /// <value>组装。</value>
        public string? Assembly { get; }

        /// <summary>
        /// 得到的类型。
        /// </summary>
        /// <value>类型。</value>
        public string Type { get; }

        /// <summary>
        /// 解除了这个实例 。
        /// </summary>
        /// <param name="assembly">组装。</param>
        /// <param name="type">类型。</param>
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
        /// 执行从 <see cref="System.ValueTuple{T1, T2}"/> 到 <see cref="QualifiedType"/>的隐含转换。
        /// </summary>
        /// <param name="args">参数。</param>
        /// <returns>转换结果。</returns>
        public static implicit operator QualifiedType((string Assembly, string Type) args) => new(args.Assembly, args.Type);

        /// <summary>
        /// 将平等的两个价值观进行比较。
        /// </summary>
        /// <param name="left">左转。</param>
        /// <param name="right">右边</param>
        /// <returns>操作员的结果。</returns>
        public static bool operator ==(QualifiedType left, QualifiedType right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// 不平等的两种价值比较。
        /// </summary>
        /// <param name="left">左转。</param>
        /// <param name="right">右边</param>
        /// <returns>操作员的结果。</returns>
        public static bool operator !=(QualifiedType left, QualifiedType right)
        {
            return !(left == right);
        }

        /// <summary>
        /// <see cref="QualifiedType"/>的平等比较器。
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
