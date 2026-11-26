using System;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Orleans.Runtime.Utilities;

/// <summary>
/// 提供条带带的边框缓冲。添加操作使用线索 ID 来索引
/// 底部缓冲线阵列,如果对 TryAdd 进行对调,则线索编号为
/// 被重新击出以选择一个不同的缓冲来重试最多三次。使用此方法
/// 以线性方式写出同时串线数 。
/// </summary>
/// <remarks>
/// 由Alex Peck从 BitFaster.Caching 中提取: https://github.com/bitfaster/BitFaster.Caching/blob/275b9b072c0218e20f549b769cd183df1374e2ee/BitFaster.Caching/Buffers/StripedMpscBuffer.cs
/// </remarks>
[DebuggerDisplay("Count = {Count}/{Capacity}")]
internal sealed class StripedMpscBuffer<T> where T : class
{
    private const int MaxAttempts = 3;

    private readonly MpscBoundedBuffer<T>[] _buffers;

    /// <summary>
    /// 初始化 StripedMpscBuffer 类的新实例,使用指定的条纹计数和缓冲大小。
    /// </summary>
    /// <param name="stripeCount">条纹计数。</param>
    /// <param name="bufferSize">缓冲大小。</param>
    public StripedMpscBuffer(int stripeCount, int bufferSize)
    {
        _buffers = new MpscBoundedBuffer<T>[stripeCount];

        for (var i = 0; i < stripeCount; i++)
        {
            _buffers[i] = new MpscBoundedBuffer<T>(bufferSize);
        }
    }

    /// <summary>
    /// 获取缓冲带中包含的项目数量。
    /// </summary>
    public int Count => _buffers.Sum(b => b.Count);

    /// <summary>
    /// 约束能力。
    /// </summary>
    public int Capacity => _buffers.Length * _buffers[0].Capacity;

    /// <summary>
    /// 将缓冲排入指定的阵列。
    /// </summary>
    /// <param name="outputBuffer">输出缓冲</param>
    /// <returns>写到输出缓冲的项数。</returns>
    /// <remarks>
    /// 单试取/排入+多次尝试添加的线索安全。
    /// </remarks>
    public int DrainTo(T[] outputBuffer) => DrainTo(outputBuffer.AsSpan());

    /// <summary>
    /// 将缓冲排入指定的范围。
    /// </summary>
    /// <param name="outputBuffer">输出缓冲</param>
    /// <returns>写到输出缓冲的项数。</returns>
    /// <remarks>
    /// 单试取/排入+多次尝试添加的线索安全。
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
    /// 尝试添加指定项目。
    /// </summary>
    /// <param name="item">增列的项目 。</param>
    /// <returns>一个 BufferStatus 值,表示操作是否成功。</returns>
    /// <remarks>
    /// 线索保险箱
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
    /// 从缓冲框中删除所有值。
    /// </summary>
    /// <remarks>
    /// 线线不安全
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
/// 提供多生产器、单一消费者的线性安全环带缓冲。当缓冲器满载时,
/// TryAdd 失败并返回假。 当缓冲区为空时, TryTake 失败并返回虚假 。
/// </summary>
/// 以咖啡因图书馆 BoundedBuffer 类为基础,使用 ben.manes@gmail.com (Ben Manes)
[DebuggerDisplay("Count = {Count}/{Capacity}")]
internal sealed class MpscBoundedBuffer<T> where T : class
{
    private T?[] _buffer;
    private readonly int _mask;
    private PaddedHeadAndTail _headAndTail; // 可变元结构, 不标记只读

    /// <summary>
    /// 初始化一个具有特定约束容量的 MpscBoundedBuffer 类新实例。
    /// </summary>
    /// <param name="boundedLength">接合长度。</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public MpscBoundedBuffer(int boundedLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedLength, 0);

        // 必须是 2 的功率, 才能使用( M) slotsMask 取代%
        boundedLength = BitOps.CeilingPowerOfTwo(boundedLength);

        _buffer = new T?[boundedLength];
        _mask = boundedLength - 1;
    }

    /// <summary>
    /// 约束能力。
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// 获取缓冲带中包含的项目数量。
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
    /// 尝试添加指定项目。
    /// </summary>
    /// <param name="item">增列的项目 。</param>
    /// <returns>一个 BufferStatus 值,表示操作是否成功。</returns>
    /// <remarks>
    /// 线索保险箱
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
    /// 尝试删除项目。
    /// </summary>
    /// <param name="item">要删除的项目。</param>
    /// <returns>一个 BufferStatus 值,表示操作是否成功。</returns>
    /// <remarks>
    /// 单试取/排入+多次尝试添加的线索安全。
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
            // 尚未出版
            return BufferStatus.Contended;
        }

        _buffer[index] = null;
        Volatile.Write(ref _headAndTail.Head, ++head);
        return BufferStatus.Success;
    }

    /// <summary>
    /// 将缓冲排入指定的数组段。
    /// </summary>
    /// <param name="output">输出缓冲</param>
    /// <returns>写到输出缓冲的项数。</returns>
    /// <remarks>
    /// 单试取/排入+多次尝试添加的线索安全。
    /// </remarks>
    public int DrainTo(ArraySegment<T> output) => DrainTo(output.AsSpan());

    /// <summary>
    /// 将缓冲排入指定的范围。
    /// </summary>
    /// <param name="output">输出缓冲</param>
    /// <returns>写到输出缓冲的项数。</returns>
    /// <remarks>
    /// 单试取/排入+多次尝试添加的线索安全。
    /// </remarks>
    public int DrainTo(Span<T> output) => DrainToImpl(output);

    // 使用外包装器方法强制 JIT 插入内部适配器方法
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
                // 尚未出版
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
    /// 从缓冲框中删除所有值。
    /// </summary>
    /// <remarks>
    /// 线线不安全
    /// </remarks>
    public void Clear()
    {
        _buffer = new T[_buffer.Length];
        _headAndTail = new PaddedHeadAndTail();
    }
}

/// <summary>
/// 指定缓冲操作状态。
/// </summary>
internal enum BufferStatus
{
    /// <summary>
    /// 缓冲带满了
    /// </summary>
    Full,

    /// <summary>
    /// 缓冲是空的 。
    /// </summary>
    Empty,

    /// <summary>
    /// 缓冲行动成功。
    /// </summary>
    Success,

    /// <summary>
    /// 缓冲行动被提出主张。
    /// </summary>
    Contended,
}

/// <summary>
/// 提供比特交织操作的实用方法。
/// </summary>
internal static class BitOps
{
    /// <summary>
    /// 计算比输入参数大2的最小功率。
    /// </summary>
    /// <param name="x">输入参数。</param>
    /// <returns>最小功率大于x或等于x的两倍。</returns>
    public static int CeilingPowerOfTwo(int x) => (int)CeilingPowerOfTwo((uint)x);

    /// <summary>
    /// 计算比输入参数大2的最小功率。
    /// </summary>
    /// <param name="x">输入参数。</param>
    /// <returns>最小功率大于x或等于x的两倍。</returns>
    public static uint CeilingPowerOfTwo(uint x) => 1u << -BitOperations.LeadingZeroCount(x - 1);

    /// <summary>
    /// 计算64位混合函数的 Stafford 变式13。
    /// </summary>
    /// <param name="z">输入参数。</param>
    /// <returns>输入参数的位组合。</returns>
    /// <remarks>
    /// 见 http://zimbry.blogspot.com/2011/09/better-bit-mixing-improving-on.html
    /// </remarks>
    public static ulong Mix64(ulong z)
    {
        z = (z ^ z >> 30) * 0xbf58476d1ce4e5b9L;
        z = (z ^ z >> 27) * 0x94d049bb133111ebL;
        return z ^ z >> 31;
    }
}

[DebuggerDisplay("Head = {Head}, Tail = {Tail}")]
[StructLayout(LayoutKind.Explicit, Size = 3 * Padding.CACHE_LINE_SIZE)] // 字段前后/之间/之后的
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
