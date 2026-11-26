using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Orleans
{
    internal class ReferenceEqualsComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// 得到一个例子 这个类。
        /// </summary>
        public static ReferenceEqualsComparer Default { get; } = new ReferenceEqualsComparer();

        /// <summary>
        /// 以参照平等(平等,《列支敦士登社会保障法》)界定了目标平等。
        /// </summary>
        /// <returns>
        /// 如果指定对象相等,则为真实;否则为虚假。
        /// </returns>
        /// <param name="x">第一个要比较的对象。</param><param name="y">第二个要比较的对象。</param>
        public new bool Equals(object? x, object? y) => object.ReferenceEquals(x, y);

        public int GetHashCode(object obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }

    internal class ReferenceEqualsComparer<T> : IEqualityComparer<T> where T : class
    {
        /// <summary>
        /// 得到一个例子 这个类。
        /// </summary>
        public static ReferenceEqualsComparer<T> Default { get; } = new ReferenceEqualsComparer<T>();

        /// <summary>
        /// 以参照平等(平等,《列支敦士登社会保障法》)界定了目标平等。
        /// </summary>
        /// <returns>
        /// 如果指定对象相等,则为真实;否则为虚假。
        /// </returns>
        /// <param name="x">第一个要比较的对象。</param><param name="y">第二个要比较的对象。</param>
        public bool Equals(T? x, T? y) => object.ReferenceEquals(x, y);

        public int GetHashCode(T obj) => obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
    }
}
