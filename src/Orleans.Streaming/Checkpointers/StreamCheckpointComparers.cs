using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Orleans.Streams
{
    /// <summary>
    /// 流检查站值的通用比较器。
    /// </summary>
    public static class StreamCheckpointComparers
    {
        /// <summary>
        /// 获得任意大小整数检查站值的比较器。
        /// </summary>
        /// <remarks>
        /// 如果任何一个值不是整数,则数值比较相同,以便无效的检查站无法前进。
        /// </remarks>
        public static IComparer<string> Numeric { get; } = Comparer<string>.Create(static (left, right) =>
        {
            return BigInteger.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftValue)
                && BigInteger.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightValue)
                    ? leftValue.CompareTo(rightValue)
                    : 0;
        });
    }
}
