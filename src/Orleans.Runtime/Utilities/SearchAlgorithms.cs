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

    // 对沿环（例如一致性哈希环）分布的范围集合进行二分查找，集合按各范围起点排序。
    // 与标准二分查找不同，此查找可以从集合开头环绕到最后一个元素。
    // 为处理从末尾环绕到开头的范围，在返回负数结果之前检查集合的最后一个元素，
    // 参见 RingRange。
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
                // 向右查找。
                left = mid + 1;
            }
            else
            {
                // 向左查找。
                right = mid - 1;
            }
        }

        // 尝试最后一个元素。
        entry = getEntry(collection, length - 1);
        if (entry.CompareTo(key) == 0)
        {
            return length - 1;
        }

#if DEBUG
        // 尝试第一个元素。
        entry = getEntry(collection, 0);
        if (entry.CompareTo(key) == 0)
        {
            Debug.Fail("Sort order invariant violated.");
        }
#endif

        return -1;
    }
}
