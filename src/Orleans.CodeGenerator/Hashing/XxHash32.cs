// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;

// 根据以下规范实现：
// https://github.com/Cyan4973/xxHash/blob/f9155bd4c57e2270a4ffbb176485e5d713de1c9b/doc/xxhash_spec.md

namespace Orleans.CodeGenerator.Hashing;

/// <summary>
/// 提供 XxHash32 算法的实现。
/// </summary>
internal sealed partial class XxHash32 : NonCryptographicHashAlgorithm
{
    private const int HashSize = sizeof(uint);
    private const int StripeSize = 4 * sizeof(uint);

    private readonly uint _seed;
    private State _state;
    private byte[]? _holdback;
    private int _length;

    /// <summary>
    /// 初始化 <see cref="XxHash32"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// XxHash32 算法支持可选种子值。
    /// 使用此构造函数创建的实例使用默认种子值 0。
    /// </remarks>
    public XxHash32()
        : this(0)
    {
    }

    /// <summary>
    /// 使用指定种子值初始化 <see cref="XxHash32"/> 类的
    /// 新实例。
    /// </summary>
    /// <param name="seed">
    /// 此实例计算时使用的哈希种子值。
    /// </param>
    public XxHash32(int seed)
        : base(HashSize)
    {
        _seed = (uint)seed;
        Reset();
    }

    /// <summary>
    /// 将哈希计算重置为初始状态。
    /// </summary>
    public override void Reset()
    {
        _state = new State(_seed);
        _length = 0;
    }

    /// <summary>
    /// 将 <paramref name="source"/> 的内容追加到当前哈希计算已经
    /// 处理的数据中。
    /// </summary>
    /// <param name="source">要处理的数据。</param>
    public override void Append(ReadOnlySpan<byte> source)
    {
        // 每读取 16 个字节就处理一个条带。
        // 长度不是 16 的整数倍的剩余数据存入暂存
        // 缓冲区。

        int held = _length & 0x0F;

        if (held != 0)
        {
            int remain = StripeSize - held;

            if (source.Length >= remain)
            {
                source.Slice(0, remain).CopyTo(_holdback.AsSpan(held));
                _state.ProcessStripe(_holdback);

                source = source.Slice(remain);
                _length += remain;
            }
            else
            {
                source.CopyTo(_holdback.AsSpan(held));
                _length += source.Length;
                return;
            }
        }

        while (source.Length >= StripeSize)
        {
            _state.ProcessStripe(source);
            source = source.Slice(StripeSize);
            _length += StripeSize;
        }

        if (source.Length > 0)
        {
            _holdback ??= new byte[StripeSize];
            source.CopyTo(_holdback);
            _length += source.Length;
        }
    }

    /// <summary>
    /// 将计算出的哈希值写入 <paramref name="destination"/>。
    /// 而不修改累积状态。
    /// </summary>
    protected override void GetCurrentHashCore(Span<byte> destination)
    {
        int remainingLength = _length & 0x0F;
        ReadOnlySpan<byte> remaining = [];

        if (remainingLength > 0)
        {
            remaining = new ReadOnlySpan<byte>(_holdback, 0, remainingLength);
        }

        uint acc = _state.Complete(_length, remaining);
        BinaryPrimitives.WriteUInt32BigEndian(destination, acc);
    }

    /// <summary>
    /// 计算提供数据的 XxHash32 哈希值。
    /// </summary>
    /// <param name="source">要计算哈希的数据。</param>
    /// <returns>提供数据的 XxHash32 哈希值。</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> 为 <see langword="null"/>。
    /// </exception>
    public static byte[] Hash(byte[] source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return Hash(new ReadOnlySpan<byte>(source));
    }

    /// <summary>
    /// 使用提供的种子值计算提供数据的 XxHash32 哈希值。
    /// </summary>
    /// <param name="source">要计算哈希的数据。</param>
    /// <param name="seed">此次哈希计算的种子值。</param>
    /// <returns>提供数据的 XxHash32 哈希值。</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> 为 <see langword="null"/>。
    /// </exception>
    public static byte[] Hash(byte[] source, int seed)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return Hash(new ReadOnlySpan<byte>(source), seed);
    }

    /// <summary>
    /// 计算提供数据的 XxHash32 哈希值。
    /// </summary>
    /// <param name="source">要计算哈希的数据。</param>
    /// <param name="seed">此次哈希计算的种子值。默认为 0。</param>
    /// <returns>提供数据的 XxHash32 哈希值。</returns>
    public static byte[] Hash(ReadOnlySpan<byte> source, int seed = 0)
    {
        byte[] ret = new byte[HashSize];
        StaticHash(source, ret, seed);
        return ret;
    }

    /// <summary>
    /// 尝试计算提供数据的 XxHash32 哈希值，并写入提供的目标缓冲区。
    /// </summary>
    /// <param name="source">要计算哈希的数据。</param>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <param name="bytesWritten">
    /// 成功时，接收写入 <paramref name="destination"/> 的字节数。
    /// </param>
    /// <param name="seed">此次哈希计算的种子值。默认为 0。</param>
    /// <returns>
    /// 返回 <see langword="true"/>，前提是 <paramref name="destination"/> 足够长，能够容纳
    /// 计算得到的哈希值（4 个字节）；否则返回 <see langword="false"/>。
    /// </returns>
    public static bool TryHash(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, int seed = 0)
    {
        if (destination.Length < HashSize)
        {
            bytesWritten = 0;
            return false;
        }

        bytesWritten = StaticHash(source, destination, seed);
        return true;
    }

    /// <summary>
    /// 计算提供数据的 XxHash32 哈希值，并写入提供的目标缓冲区。
    /// </summary>
    /// <param name="source">要计算哈希的数据。</param>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <param name="seed">此次哈希计算的种子值。默认为 0。</param>
    /// <returns>
    /// 写入 <paramref name="destination"/> 的字节数。
    /// </returns>
    public static int Hash(ReadOnlySpan<byte> source, Span<byte> destination, int seed = 0)
    {
        if (destination.Length < HashSize)
            throw new ArgumentException(@"destination too short", nameof(destination));

        return StaticHash(source, destination, seed);
    }

    private static int StaticHash(ReadOnlySpan<byte> source, Span<byte> destination, int seed)
    {
        int totalLength = source.Length;
        State state = new State((uint)seed);

        while (source.Length >= StripeSize)
        {
            state.ProcessStripe(source);
            source = source.Slice(StripeSize);
        }

        uint val = state.Complete(totalLength, source);
        BinaryPrimitives.WriteUInt32BigEndian(destination, val);
        return HashSize;
    }
}
