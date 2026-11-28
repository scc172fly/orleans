namespace System.Collections.Generic;

/// <summary>
/// 可枚举对象的辅助扩展方法。
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// 普通 foreach 循环的简写形式。
    /// </summary>
    /// <typeparam name="T">元素的类型。</typeparam>
    /// <param name="enumerable">要遍历的可枚举对象。</param>
    /// <param name="action">对每个元素执行的操作。</param>
    public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
    {
        foreach (var item in enumerable)
        {
            action(item);
        }
    }
}
