using System.Runtime.InteropServices;
using System.Threading;

namespace Orleans.Caching.Internal;

/// <summary>
/// 按 CPU 缓存行大小填充的 long 值，用于缓解伪共享。
/// </summary>
// Derived from BitFaster.Caching by Alex Peck
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Counters/PaddedLong.cs
[StructLayout(LayoutKind.Explicit, Size = 2 * Padding.CACHE_LINE_SIZE)] // 字段之前、之间及之后的填充。
internal struct PaddedLong
{
    /// <summary>
    /// 值。
    /// </summary>
    [FieldOffset(Padding.CACHE_LINE_SIZE)] public long Value;

    /// <summary>
    /// 读取字段的值，并在需要的系统上插入内存屏障，
    /// 防止内存操作重排序。
    /// </summary>
    /// <returns>读取到的值。</returns>
    public long VolatileRead() => Volatile.Read(ref Value);

    /// <summary>
    /// 将当前值与预期值比较，如果相等则替换当前值。
    /// </summary>
    /// <param name="expected">预期值。</param>
    /// <param name="updated">更新后的值。</param>
    /// <returns>值已更新时为 true，否则为 false。</returns>
    public bool CompareAndSwap(long expected, long updated) => Interlocked.CompareExchange(ref Value, updated, expected) == expected;
}
