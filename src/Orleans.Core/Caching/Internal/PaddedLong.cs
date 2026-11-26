using System.Runtime.InteropServices;
using System.Threading;

namespace Orleans.Caching.Internal;

/// <summary>
/// CPU缓存线大小的长值添加,以减少虚假共享。
/// </summary>
// 由Alex Peck从 BitFaster.Caching 取出
// https://github.com/bitfaster/BitFaster.Caching/blob/5b2d64a1afcc251787fbe231c6967a62820fc93c/BitFaster.Caching/Counters/PaddedLong.cs
[StructLayout(LayoutKind.Explicit, Size = 2 * Padding.CACHE_LINE_SIZE)] // 字段前后/之间/之后的
internal struct PaddedLong
{
    /// <summary>
    /// 价值。
    /// </summary>
    [FieldOffset(Padding.CACHE_LINE_SIZE)] public long Value;

    /// <summary>
    /// 读取字段的价值,以及需要它插入内存屏障的系统 
    /// 防止重新排列记忆操作。
    /// </summary>
    /// <returns>所读取的值。</returns>
    public long VolatileRead() => Volatile.Read(ref Value);

    /// <summary>
    /// 将当前值与预期值进行比较,如果等于则替换当前值。
    /// </summary>
    /// <param name="expected">预期值。</param>
    /// <param name="updated">更新价值。</param>
    /// <returns>如果更新了数值, 则 True, 否则是虚假的。</returns>
    public bool CompareAndSwap(long expected, long updated) => Interlocked.CompareExchange(ref Value, updated, expected) == expected;
}
