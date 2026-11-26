// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;

namespace Orleans.CodeGenerator.Hashing;

/// <summary>
///   代表非加密散列算法。
/// </summary>
internal abstract class NonCryptographicHashAlgorithm
{
    /// <summary>
    ///   获取从这个散列算法中生成的字节数 。
    /// </summary>
    /// <value>从这个散列算法中生成的字节数。</value>
    public int HashLengthInBytes { get; }

    /// <summary>
    ///   由衍生类别中建筑商调来启动
    ///   <see cref="NonCryptographicHashAlgorithm"/> 类.
    /// </summary>
    /// <param name="hashLengthInBytes">
    ///   由这个散列算法生成的字节数 。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <paramref name="hashLengthInBytes"/> 不到1。
    /// </exception>
    protected NonCryptographicHashAlgorithm(int hashLengthInBytes)
    {
        if (hashLengthInBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(hashLengthInBytes));

        HashLengthInBytes = hashLengthInBytes;
    }

    /// <summary>
    ///   当从阶级中被推翻的时候,
    ///   将 <paramref name="source"/> 的内容附加到数据中
    ///   用于当前散列计算处理。
    /// </summary>
    /// <param name="source">要处理的数据 。</param>
    public abstract void Append(ReadOnlySpan<byte> source);

    /// <summary>
    ///   当从阶级中被推翻的时候,
    ///   将散列计算重新切换到初始状态。
    /// </summary>
    public abstract void Reset();

    /// <summary>
    ///   当从阶级中被推翻的时候,
    ///   将计算成散列值写入 <paramref name="destination"/>
    ///   但不修改累积状态。
    /// </summary>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <remarks>
    ///   <para>
    ///     采用这种方法必须精确写
    ///     <see cref="HashLengthInBytes"/> 字节为 <paramref name="destination"/>。
    ///     不可假设缓冲区是零初始化的。
    ///   </para>
    ///   <para>
    ///     <see cref="NonCryptographicHashAlgorithm"/> 类验证
    ///     调用此方法前的缓冲点大小,然后切除
    ///     以精确的长度 <see cref="HashLengthInBytes"/> 向下。
    ///   </para>
    /// </remarks>
    protected abstract void GetCurrentHashCore(Span<byte> destination);

    /// <summary>
    ///   将 <paramref name="source"/> 的内容附加到已经的数据中
    ///   用于当前散列计算处理。
    /// </summary>
    /// <param name="source">要处理的数据 。</param>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="source"/> 是 <see langword="null"/>。
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
    ///   将 <paramref name="stream"/> 的内容附加到已经的数据中
    ///   用于当前散列计算处理。
    /// </summary>
    /// <param name="stream">要处理的数据 。</param>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="stream"/> 是 <see langword="null"/>。
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
    ///   却不时读取 <paramref name="stream"/>
    ///   并把它们附加到数据上
    ///   用于当前散列计算处理。
    /// </summary>
    /// <param name="stream">要处理的数据 。</param>
    /// <param name="cancellationToken">
    ///   用于监测取消请求的标记 。
    ///   默认值为 <see cref="CancellationToken.None"/>。
    /// </param>
    /// <returns>
    ///   代表非同步附加操作的任务 。
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="stream"/> 是 <see langword="null"/>。
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
    ///   获取当前计算成散列值,而不修改累积状态。
    /// </summary>
    /// <returns>
    ///   已经提供数据的散列值 。
    /// </returns>
    public byte[] GetCurrentHash()
    {
        byte[] ret = new byte[HashLengthInBytes];
        GetCurrentHashCore(ret);
        return ret;
    }

    /// <summary>
    ///   试图将计算成散列值写入 <paramref name="destination"/>
    ///   但不修改累积状态。
    /// </summary>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <param name="bytesWritten">
    ///   成功时, 收到写到 <paramref name="destination"/>的字节数 。
    /// </param>
    /// <returns>
    ///   <see langword="true"/> 如果 <paramref name="destination"/> 足够长的时间来接收
    ///   计算散列值;否则, <see langword="false"/>。
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
    ///   将计算成散列值写入 <paramref name="destination"/>
    ///   但不修改累积状态。
    /// </summary>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <returns>
    ///   写入 <paramref name="destination"/>的字节数,
    ///   总是 <see cref="HashLengthInBytes"/>。
    /// </returns>
    /// <exception cref="ArgumentException">
    ///   <paramref name="destination"/> 短于 <see cref="HashLengthInBytes"/>。
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
    ///   获取当前计算出的散列值,并清除累积状态。
    /// </summary>
    /// <returns>
    ///   已经提供数据的散列值 。
    /// </returns>
    public byte[] GetHashAndReset()
    {
        byte[] ret = new byte[HashLengthInBytes];
        GetHashAndResetCore(ret);
        return ret;
    }

    /// <summary>
    ///   试图将计算成散列值写入 <paramref name="destination"/>。
    ///   如果成功,则清除累积状态。
    /// </summary>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <param name="bytesWritten">
    ///   成功时, 收到写到 <paramref name="destination"/>的字节数 。
    /// </param>
    /// <returns>
    ///   <see langword="true"/> 并清除累计状态
    ///   如果 <paramref name="destination"/> 足够长,可以接受
    ///   计算散列值;否则, <see langword="false"/>。
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
    ///   将计算成散列值写入 <paramref name="destination"/>
    ///   然后清除累积状态。
    /// </summary>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <returns>
    ///   写入 <paramref name="destination"/>的字节数,
    ///   总是 <see cref="HashLengthInBytes"/>。
    /// </returns>
    /// <exception cref="ArgumentException">
    ///   <paramref name="destination"/> 短于 <see cref="HashLengthInBytes"/>。
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
    ///   将计算成散列值写入 <paramref name="destination"/>
    ///   然后清除累积状态。
    /// </summary>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <remarks>
    ///   <para>
    ///     采用这种方法必须精确写
    ///     <see cref="HashLengthInBytes"/> 字节为 <paramref name="destination"/>。
    ///     不可假设缓冲区是零初始化的。
    ///   </para>
    ///   <para>
    ///     <see cref="NonCryptographicHashAlgorithm"/> 类验证
    ///     调用此方法前的缓冲点大小,然后切除
    ///     以精确的长度 <see cref="HashLengthInBytes"/> 向下。
    ///   </para>
    ///   <para>
    ///     默认使用此方法调用
    ///     <see cref="GetCurrentHashCore"/> 之后是 <see cref="Reset"/>。
    ///     这种方法的重叠不需要使用这两种方法中的任何一个,
    ///     但要保证打电话的人不能观察到行为上的差别
    ///   </para>
    /// </remarks>
    protected virtual void GetHashAndResetCore(Span<byte> destination)
    {
        Debug.Assert(destination.Length == HashLengthInBytes);

        GetCurrentHashCore(destination);
        Reset();
    }

    /// <summary>
    ///   这种方法不支持,也不应该被称作。
    ///   调用 <see cref="GetCurrentHash()"/> 或 <see cref="GetHashAndReset()"/>
    ///   而不是。
    /// </summary>
    /// <returns>此方法总是丢弃 <see cref="NotSupportedException"/>。</returns>
    /// <exception cref="NotSupportedException">在任何情况下</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Use GetCurrentHash() to retrieve the computed hash code.", true)]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
    public override int GetHashCode()
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
    {
        throw new NotSupportedException("GetHashCode");
    }
}
