using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Orleans
{
    internal class ReferenceEqualsComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// 获取此类的实例。
        /// </summary>
        public static ReferenceEqualsComparer Default { get; } = new ReferenceEqualsComparer();

        /// <summary>
        /// 通过引用相等性定义对象相等性（相当于 LISP 中的 eq）。
        /// </summary>
        /// <returns>
        /// 指定对象相等时为 true；否则为 false。
        /// </returns>
        /// <param name="x">要比较的第一个对象。</param><param name="y">要比较的第二个对象。</param>
        public new bool Equals(object? x, object? y) => object.ReferenceEquals(x, y);

        public int GetHashCode(object obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }

    internal class ReferenceEqualsComparer<T> : IEqualityComparer<T> where T : class
    {
        /// <summary>
        /// 获取此类的实例。
        /// </summary>
        public static ReferenceEqualsComparer<T> Default { get; } = new ReferenceEqualsComparer<T>();

        /// <summary>
        /// 通过引用相等性定义对象相等性（相当于 LISP 中的 eq）。
        /// </summary>
        /// <returns>
        /// 指定对象相等时为 true；否则为 false。
        /// </returns>
        /// <param name="x">要比较的第一个对象。</param><param name="y">要比较的第二个对象。</param>
        public bool Equals(T? x, T? y) => object.ReferenceEquals(x, y);

        public int GetHashCode(T obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }
}
