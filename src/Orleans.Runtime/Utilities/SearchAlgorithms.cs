using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Utilities;

internal static class SearchAlgorithms
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BinarySearch<TState>(int length, TState state, Func<int, TState, int> comparer)
    {
        var left = 0;
        var right = length - 1;

        while (left <= right)
        {
            var mid = left + (right - left) / 2;
            var comparison = comparer(mid, state);

            if (comparison == 0)
            {
                return mid;
            }
            else if (comparison < 0)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return -1;
    }

    // 以一个环(例如,一个一致的散列环)为基准,按每个范围的起点排序,对环(如,一个连续的散列环)上的靶场收藏进行二进制搜索。
    // 这与标准的二进制搜索有所不同, 因为搜索可以从开始到收藏中最后一个元素的周围环绕。
    // 这一点通过在返回负结果之前核对收藏中最后一项内容,以适应上述情况,处理以下案件:
    // 区域从结束到开始环绕。见 RingRange
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RingRangeBinarySearch<TCollection, TElement, TKey>(
        int length,
        TCollection collection,
        Func<TCollection, int, TElement> getEntry,
        TKey key) where TElement : IComparable<TKey>
    {
        if (length == 0) return -1;

        var left = 0;
        var right = length - 1;

        TElement entry;
        while (left <= right)
        {
            var mid = left + (right - left) / 2;
            entry = getEntry(collection, mid);
            var comparison = entry.CompareTo(key);

            if (comparison == 0)
            {
                return mid;
            }
            else if (comparison < 0)
            {
                // 右转
                left = mid + 1;
            }
            else
            {
                // 左转
                right = mid - 1;
            }
        }

        // 试一下最后一个元素。
        entry = getEntry(collection, length - 1);
        if (entry.CompareTo(key) == 0)
        {
            return length - 1;
        }

#if DEBUG
        // 试一下第一个元素。
        entry = getEntry(collection, 0);
        if (entry.CompareTo(key) == 0)
        {
            Debug.Fail("Sort order invariant violated.");
        }
#endif

        return -1;
    }
}
