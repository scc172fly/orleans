// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;

namespace Orleans.CodeGenerator.Hashing;

/// <summary>
/// 表示非加密哈希算法。
/// </summary>
internal abstract class NonCryptographicHashAlgorithm
{
    /// <summary>
    /// 获取此哈希算法生成的字节数。
    /// </summary>
    /// <value>此哈希算法生成的字节数。</value>
    public int HashLengthInBytes { get; }

    /// <summary>
    /// 由派生类的构造函数调用，用于初始化
    /// <see cref="NonCryptographicHashAlgorithm"/> 类。
    /// </summary>
    /// <param name="hashLengthInBytes">
    /// 此哈希算法生成的字节数。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="hashLengthInBytes"/> 小于 1。
    /// </exception>
    protected NonCryptographicHashAlgorithm(int hashLengthInBytes)
    {
        if (hashLengthInBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(hashLengthInBytes));

        HashLengthInBytes = hashLengthInBytes;
    }

    /// <summary>
    /// 在派生类中重写时，
    /// 将 <paramref name="source"/> 的内容追加到当前哈希计算已经
    /// 处理的数据中。
    /// </summary>
    /// <param name="source">要处理的数据。</param>
    public abstract void Append(ReadOnlySpan<byte> source);

    /// <summary>
    /// 在派生类中重写时，
    /// 将哈希计算重置为初始状态。
    /// </summary>
    public abstract void Reset();

    /// <summary>
    /// 在派生类中重写时，
    /// 将计算得到的哈希值写入 <paramref name="destination"/>，
    /// 而不修改累积状态。
    /// </summary>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <remarks>
    ///   <para>
    /// 此方法的实现必须恰好写入
    /// <see cref="HashLengthInBytes"/> 个字节到 <paramref name="destination"/>。
    /// 不要假设缓冲区已经初始化为零。
    ///   </para>
    ///   <para>
    /// <see cref="NonCryptographicHashAlgorithm"/> 类在调用此方法之前验证
    /// 缓冲区大小，并将 span 切分为
    /// 长度恰好为 <see cref="HashLengthInBytes"/> 的切片。
    ///   </para>
    /// </remarks>
    protected abstract void GetCurrentHashCore(Span<byte> destination);

    /// <summary>
    /// 将 <paramref name="source"/> 的内容追加到当前哈希计算已经
    /// 处理的数据中。
    /// </summary>
    /// <param name="source">要处理的数据。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> 为 <see langword="null"/>。
    /// </exception>
    public void Append(byte[] source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Append(new ReadOnlySpan<byte>(source));
    }

    /// <summary>
    /// 将 <paramref name="stream"/> 的内容追加到当前哈希计算已经
    /// 处理的数据中。
    /// </summary>
    /// <param name="stream">要处理的数据。</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> 为 <see langword="null"/>。
    /// </exception>
    /// <seealso cref="AppendAsync(Stream, CancellationToken)"/>
    public void Append(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);

            if (read == 0)
            {
                break;
            }

            Append(new ReadOnlySpan<byte>(buffer, 0, read));
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <summary>
    /// 异步读取 <paramref name="stream"/> 的内容，
    /// 并将其追加到当前哈希计算已经
    /// 处理的数据中。
    /// </summary>
    /// <param name="stream">要处理的数据。</param>
    /// <param name="cancellationToken">
    /// 用于监视取消请求的令牌。
    /// 默认值为 <see cref="CancellationToken.None"/>。
    /// </param>
    /// <returns>
    /// 表示异步追加操作的任务。
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> 为 <see langword="null"/>。
    /// </exception>
    public Task AppendAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        return AppendAsyncCore(stream, cancellationToken);
    }

    private async Task AppendAsyncCore(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

        while (true)
        {
#if NETCOREAPP
            int read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
            int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#endif

            if (read == 0)
            {
                break;
            }

            Append(new ReadOnlySpan<byte>(buffer, 0, read));
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <summary>
    /// 获取当前计算得到的哈希值，不修改累积状态。
    /// </summary>
    /// <returns>
    /// 已提供数据的哈希值。
    /// </returns>
    public byte[] GetCurrentHash()
    {
        byte[] ret = new byte[HashLengthInBytes];
        GetCurrentHashCore(ret);
        return ret;
    }

    /// <summary>
    /// 尝试将计算得到的哈希值写入 <paramref name="destination"/>，
    /// 而不修改累积状态。
    /// </summary>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <param name="bytesWritten">
    /// 成功时，接收写入 <paramref name="destination"/> 的字节数。
    /// </param>
    /// <returns>
    /// 返回 <see langword="true"/>，前提是 <paramref name="destination"/> 足够长，能够容纳
    /// 计算得到的哈希值；否则为 <see langword="false"/>。
    /// </returns>
    public bool TryGetCurrentHash(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < HashLengthInBytes)
        {
            bytesWritten = 0;
            return false;
        }

        GetCurrentHashCore(destination.Slice(0, HashLengthInBytes));
        bytesWritten = HashLengthInBytes;
        return true;
    }

    /// <summary>
    /// 将计算出的哈希值写入 <paramref name="destination"/>。
    /// 而不修改累积状态。
    /// </summary>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <returns>
    /// 写入 <paramref name="destination"/> 的字节数，
    /// 始终为 <see cref="HashLengthInBytes"/>。
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> 的长度小于 <see cref="HashLengthInBytes"/>。
    /// </exception>
    public int GetCurrentHash(Span<byte> destination)
    {
        if (destination.Length < HashLengthInBytes)
        {
            throw new ArgumentException(@"destination too short", nameof(destination));
        }

        GetCurrentHashCore(destination.Slice(0, HashLengthInBytes));
        return HashLengthInBytes;
    }

    /// <summary>
    /// 获取当前计算得到的哈希值，并清除累积状态。
    /// </summary>
    /// <returns>
    /// 已提供数据的哈希值。
    /// </returns>
    public byte[] GetHashAndReset()
    {
        byte[] ret = new byte[HashLengthInBytes];
        GetHashAndResetCore(ret);
        return ret;
    }

    /// <summary>
    /// 尝试将计算得到的哈希值写入 <paramref name="destination"/>。
    /// 成功时清除累积状态。
    /// </summary>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <param name="bytesWritten">
    /// 成功时，接收写入 <paramref name="destination"/> 的字节数。
    /// </param>
    /// <returns>
    /// 返回 <see langword="true"/> 并清除累积状态，
    /// 前提是 <paramref name="destination"/> 足够长，能够容纳
    /// 计算得到的哈希值；否则返回 <see langword="false"/>。
    /// </returns>
    public bool TryGetHashAndReset(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < HashLengthInBytes)
        {
            bytesWritten = 0;
            return false;
        }

        GetHashAndResetCore(destination.Slice(0, HashLengthInBytes));
        bytesWritten = HashLengthInBytes;
        return true;
    }

    /// <summary>
    /// 将计算出的哈希值写入 <paramref name="destination"/>。
    /// 然后清除累积状态。
    /// </summary>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <returns>
    /// 写入 <paramref name="destination"/> 的字节数，
    /// 始终为 <see cref="HashLengthInBytes"/>。
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> 的长度小于 <see cref="HashLengthInBytes"/>。
    /// </exception>
    public int GetHashAndReset(Span<byte> destination)
    {
        if (destination.Length < HashLengthInBytes)
        {
            throw new ArgumentException(@"destination too short", nameof(destination));
        }

        GetHashAndResetCore(destination.Slice(0, HashLengthInBytes));
        return HashLengthInBytes;
    }

    /// <summary>
    /// 将计算出的哈希值写入 <paramref name="destination"/>。
    /// 然后清除累积状态。
    /// </summary>
    /// <param name="destination">接收计算所得哈希值的缓冲区。</param>
    /// <remarks>
    ///   <para>
    /// 此方法的实现必须恰好写入
    /// <see cref="HashLengthInBytes"/> 个字节到 <paramref name="destination"/>。
    /// 不要假设缓冲区已经初始化为零。
    ///   </para>
    ///   <para>
    /// <see cref="NonCryptographicHashAlgorithm"/> 类在调用此方法之前验证
    /// 缓冲区大小，并将 span 切分为
    /// 长度恰好为 <see cref="HashLengthInBytes"/> 的切片。
    ///   </para>
    ///   <para>
    /// 此方法的默认实现调用
    /// <see cref="GetCurrentHashCore"/>，然后调用 <see cref="Reset"/>。
    /// 此方法的重写无需调用这两个方法中的任何一个，
    /// 但必须确保调用方无法观察到行为上的差异。
    ///   </para>
    /// </remarks>
    protected virtual void GetHashAndResetCore(Span<byte> destination)
    {
        Debug.Assert(destination.Length == HashLengthInBytes);

        GetCurrentHashCore(destination);
        Reset();
    }

    /// <summary>
    /// 此方法不受支持，不应调用。
    /// 请改用 <see cref="GetCurrentHash()"/> 或 <see cref="GetHashAndReset()"/>，
    /// 而不要调用此方法。
    /// </summary>
    /// <returns>此方法始终抛出 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">所有情况下均会抛出。</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Use GetCurrentHash() to retrieve the computed hash code.", true)]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
    public override int GetHashCode()
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
    {
        throw new NotSupportedException("GetHashCode");
    }
}
