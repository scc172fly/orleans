using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Orleans.Streams
{
    /// <summary>
    /// 流检查点值的通用比较器。
    /// </summary>
    public static class StreamCheckpointComparers
    {
        /// <summary>
        /// 获取适用于任意大小整数检查点值的比较器。
        /// </summary>
        /// <remarks>
        /// 如果任一值不是整数，则比较结果为相等，从而防止无效检查点推进。
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
