namespace System.Collections.Generic;

/// <summary>
/// 数字的辅助扩展名。
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// 短手论坛 普通的每个。
    /// </summary>
    /// <typeparam name="T">元素的类型。</typeparam>
    /// <param name="enumerable">可供循环的数值。</param>
    /// <param name="action">每一元素申请的行动。</param>
    public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
    {
        foreach (var item in enumerable)
        {
            action(item);
        }
    }
}
