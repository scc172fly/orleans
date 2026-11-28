using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Utilities;

/// <summary>
/// 提供分段有界缓冲区。添加操作使用线程 ID 作为索引来访问
/// 底层缓冲区数组。如果 TryAdd 发生竞争，则对线程 ID
/// 重新进行哈希计算，以选择其他缓冲区，最多重试 3 次。采用这种方式，
/// 写入性能会随并发线程数量线性扩展。
/// </summary>
/// <remarks>
/// Derived from BitFaster.Caching by Alex Peck: https://github.com/bitfaster/BitFaster.Caching/blob/275b9b072c0218e20f549b769cd183df1374e2ee/BitFaster.Caching/Buffers/StripedMpscBuffer.cs
/// </remarks>
[DebuggerDisplay("Count = {Count}/{Capacity}")]
internal sealed class StripedMpscBuffer<T> where T : class
{
    private const int MaxAttempts = 3;

    private readonly MpscBoundedBuffer<T>[] _buffers;

    /// <summary>
    /// 使用指定分段数量和缓冲区大小初始化 StripedMpscBuffer 类的新实例。
    /// </summary>
    /// <param name="stripeCount">分段数量。</param>
    /// <param name="bufferSize">缓冲区大小。</param>
    public StripedMpscBuffer(int stripeCount, int bufferSize)
    {
        _buffers = new MpscBoundedBuffer<T>[stripeCount];

        for (var i = 0; i < stripeCount; i++)
        {
            _buffers[i] = new MpscBoundedBuffer<T>(bufferSize);
        }
    }

    /// <summary>
    /// 获取缓冲区中包含的元素数量。
    /// </summary>
    public int Count => _buffers.Sum(b => b.Count);

    /// <summary>
    /// 容量上限。
    /// </summary>
    public int Capacity => _buffers.Length * _buffers[0].Capacity;

    /// <summary>
    /// 将缓冲区中的内容排出到指定数组。
    /// </summary>
    /// <param name="outputBuffer">输出缓冲区。</param>
    /// <returns>写入输出缓冲区的数据项数量。</returns>
    /// <remarks>
    /// 在单个尝试取出或排空操作与多个尝试添加操作并发进行时具有线程安全性。
    /// </remarks>
    public int DrainTo(T[] outputBuffer) => DrainTo(outputBuffer.AsSpan());

    /// <summary>
    /// 将缓冲区中的内容排出到指定 Span。
    /// </summary>
    /// <param name="outputBuffer">输出缓冲区。</param>
    /// <returns>写入输出缓冲区的数据项数量。</returns>
    /// <remarks>
    /// 在单个尝试取出或排空操作与多个尝试添加操作并发进行时具有线程安全性。
    /// </remarks>
    public int DrainTo(Span<T> outputBuffer)
    {
        var count = 0;

        for (var i = 0; i < _buffers.Length; i++)
        {
            if (count == outputBuffer.Length)
            {
                break;
            }

            var segment = outputBuffer[count..];

            count += _buffers[i].DrainTo(segment);
        }

        return count;
    }

    /// <summary>
    /// 尝试添加指定元素。
    /// </summary>
    /// <param name="item">要添加的元素。</param>
    /// <returns>指示操作是否成功的 BufferStatus 值。</returns>
    /// <remarks>
    /// 线程安全。
    /// </remarks>
    public BufferStatus TryAdd(T item)
    {
        var z = BitOps.Mix64((ulong)Environment.CurrentManagedThreadId);
        var inc = (int)(z >> 32) | 1;
        var h = (int)z;

        var mask = _buffers.Length - 1;

        var result = BufferStatus.Empty;

        for (var i = 0; i < MaxAttempts; i++)
        {
            result = _buffers[h & mask].TryAdd(item);

            if (result == BufferStatus.Success)
            {
                break;
            }

            h += inc;
        }

        return result;
    }

    /// <summary>
    /// 移除缓冲区中的所有值。
    /// </summary>
    /// <remarks>
    /// 非线程安全。
    /// </remarks>
    public void Clear()
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            _buffers[i].Clear();
        }
    }
}

/// <summary>
/// 提供多生产者、单消费者的线程安全环形缓冲区。当缓冲区已满时，
/// TryAdd 失败并返回 false。当缓冲区为空时，TryTake 失败并返回 false。
/// </summary>
/// Based on the BoundedBuffer class in the Caffeine library by ben.manes@gmail.com (Ben Manes).
[DebuggerDisplay("Count = {Count}/{Capacity}")]
internal sealed class MpscBoundedBuffer<T> where T : class
{
    private T?[] _buffer;
    private readonly int _mask;
    private PaddedHeadAndTail _headAndTail; // 可变结构体，不要标记为 readonly。

    /// <summary>
    /// 使用指定容量上限初始化 MpscBoundedBuffer 类的新实例。
    /// </summary>
    /// <param name="boundedLength">长度上限。</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public MpscBoundedBuffer(int boundedLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedLength, 0);

        // 必须是 2 的幂，才能使用 & slotsMask 代替 % 取模。
        boundedLength = BitOps.CeilingPowerOfTwo(boundedLength);

        _buffer = new T?[boundedLength];
        _mask = boundedLength - 1;
    }

    /// <summary>
    /// 容量上限。
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// 获取缓冲区中包含的元素数量。
    /// </summary>
    public int Count
    {
        get
        {
            var spinner = new SpinWait();
            while (true)
            {
                var headNow = Volatile.Read(ref _headAndTail.Head);
                var tailNow = Volatile.Read(ref _headAndTail.Tail);

                if (headNow == Volatile.Read(ref _headAndTail.Head) &&
                    tailNow == Volatile.Read(ref _headAndTail.Tail))
                {
                    return GetCount(headNow, tailNow);
                }

                spinner.SpinOnce();
            }
        }
    }

    private int GetCount(int head, int tail)
    {
        if (head != tail)
        {
            head &= _mask;
            tail &= _mask;

            return head < tail ? tail - head : _buffer.Length - head + tail;
        }
        return 0;
    }

    /// <summary>
    /// 尝试添加指定元素。
    /// </summary>
    /// <param name="item">要添加的元素。</param>
    /// <returns>指示操作是否成功的 BufferStatus 值。</returns>
    /// <remarks>
    /// 线程安全。
    /// </remarks>
    public BufferStatus TryAdd(T item)
    {
        int head = Volatile.Read(ref _headAndTail.Head);
        int tail = _headAndTail.Tail;
        int size = tail - head;

        if (size >= _buffer.Length)
        {
            return BufferStatus.Full;
        }

        if (Interlocked.CompareExchange(ref _headAndTail.Tail, tail + 1, tail) == tail)
        {
            int index = tail & _mask;
            Volatile.Write(ref _buffer[index], item);

            return BufferStatus.Success;
        }

        return BufferStatus.Contended;
    }


    /// <summary>
    /// 尝试移除一个元素。
    /// </summary>
    /// <param name="item">要移除的元素。</param>
    /// <returns>指示操作是否成功的 BufferStatus 值。</returns>
    /// <remarks>
    /// 在单个尝试取出或排空操作与多个尝试添加操作并发进行时具有线程安全性。
    /// </remarks>
    public BufferStatus TryTake(out T? item)
    {
        int head = Volatile.Read(ref _headAndTail.Head);
        int tail = _headAndTail.Tail;
        int size = tail - head;

        if (size == 0)
        {
            item = default;
            return BufferStatus.Empty;
        }

        int index = head & _mask;

        item = Volatile.Read(ref _buffer[index]);

        if (item == null)
        {
            // 尚未发布。
            return BufferStatus.Contended;
        }

        _buffer[index] = null;
        Volatile.Write(ref _headAndTail.Head, ++head);
        return BufferStatus.Success;
    }

    /// <summary>
    /// 将缓冲区中的内容排出到指定数组段。
    /// </summary>
    /// <param name="output">输出缓冲区。</param>
    /// <returns>写入输出缓冲区的数据项数量。</returns>
    /// <remarks>
    /// 在单个尝试取出或排空操作与多个尝试添加操作并发进行时具有线程安全性。
    /// </remarks>
    public int DrainTo(ArraySegment<T> output) => DrainTo(output.AsSpan());

    /// <summary>
    /// 将缓冲区中的内容排出到指定 Span。
    /// </summary>
    /// <param name="output">输出缓冲区。</param>
    /// <returns>写入输出缓冲区的数据项数量。</returns>
    /// <remarks>
    /// 在单个尝试取出或排空操作与多个尝试添加操作并发进行时具有线程安全性。
    /// </remarks>
    public int DrainTo(Span<T> output) => DrainToImpl(output);

    // 使用外层包装方法强制 JIT 内联内部适配器方法。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DrainToImpl(Span<T> output)
    {
        int head = Volatile.Read(ref _headAndTail.Head);
        int tail = _headAndTail.Tail;
        int size = tail - head;

        if (size == 0)
        {
            return 0;
        }

        var localBuffer = _buffer.AsSpan();

        int outCount = 0;

        do
        {
            int index = head & _mask;

            T? item = Volatile.Read(ref localBuffer[index]);

            if (item == null)
            {
                // 尚未发布。
                break;
            }

            localBuffer[index] = null;
            Write(output, outCount++, item);
            head++;
        }
        while (head != tail && outCount < Length(output));

        _headAndTail.Head = head;

        return outCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write(Span<T> output, int index, T item) => output[index] = item;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Length(ReadOnlySpan<T> output) => output.Length;

    /// <summary>
    /// 移除缓冲区中的所有值。
    /// </summary>
    /// <remarks>
    /// 非线程安全。
    /// </remarks>
    public void Clear()
    {
        _buffer = new T[_buffer.Length];
        _headAndTail = new PaddedHeadAndTail();
    }
}

/// <summary>
/// 指定缓冲区操作的状态。
/// </summary>
internal enum BufferStatus
{
    /// <summary>
    /// 缓冲区已满。
    /// </summary>
    Full,

    /// <summary>
    /// 缓冲区为空。
    /// </summary>
    Empty,

    /// <summary>
    /// 缓冲区操作成功。
    /// </summary>
    Success,

    /// <summary>
    /// 缓冲区操作发生竞争。
    /// </summary>
    Contended,
}

/// <summary>
/// 提供用于位运算的实用方法。
/// </summary>
internal static class BitOps
{
    /// <summary>
    /// 计算大于输入参数的最小 2 的幂。
    /// </summary>
    /// <param name="x">输入参数。</param>
    /// <returns>大于或等于 x 的最小 2 的幂。</returns>
    public static int CeilingPowerOfTwo(int x) => (int)CeilingPowerOfTwo((uint)x);

    /// <summary>
    /// 计算大于输入参数的最小 2 的幂。
    /// </summary>
    /// <param name="x">输入参数。</param>
    /// <returns>大于或等于 x 的最小 2 的幂。</returns>
    public static uint CeilingPowerOfTwo(uint x) => 1u << -BitOperations.LeadingZeroCount(x - 1);

    /// <summary>
    /// 计算 64 位混合函数的 Stafford 变体 13。
    /// </summary>
    /// <param name="z">输入参数。</param>
    /// <returns>对输入参数进行位混合后的结果。</returns>
    /// <remarks>
    /// 参见 http://zimbry.blogspot.com/2011/09/better-bit-mixing-improving-on.html
    /// </remarks>
    public static ulong Mix64(ulong z)
    {
        z = (z ^ z >> 30) * 0xbf58476d1ce4e5b9L;
        z = (z ^ z >> 27) * 0x94d049bb133111ebL;
        return z ^ z >> 31;
    }
}

[DebuggerDisplay("Head = {Head}, Tail = {Tail}")]
[StructLayout(LayoutKind.Explicit, Size = 3 * Padding.CACHE_LINE_SIZE)] // 字段前、字段之间及字段后的填充。
internal struct PaddedHeadAndTail
{
    [FieldOffset(1 * Padding.CACHE_LINE_SIZE)] public int Head;
    [FieldOffset(2 * Padding.CACHE_LINE_SIZE)] public int Tail;
}

internal static class Padding
{
#if TARGET_ARM64 || TARGET_LOONGARCH64
        internal const int CACHE_LINE_SIZE = 128;
#else
    internal const int CACHE_LINE_SIZE = 64;
#endif
}
