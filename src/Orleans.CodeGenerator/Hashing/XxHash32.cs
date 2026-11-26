// 根据一项或多项协定获得 .NET 基金会许可证。
// 下 .NET 基金会根据麻省理工学院的许可证 将这个文件发给你

using System.Buffers.Binary;

// 执行从
// https://github.com/Cyan4973/xxHash/blob/f9155bd4c57e2270a4ffbb176485e5d713de1c9b/doc/xxhash_spec.md

namespace Orleans.CodeGenerator.Hashing;

/// <summary>
///   提供 XxHash32 算法的实施。
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
    ///   初始化 <see cref="XxHash32"/> 类的新实例。
    /// </summary>
    /// <remarks>
    ///   XxHash32 算法支持可选种子值。
    ///   由此构建器创建的实例使用默认种子, 零。
    /// </remarks>
    public XxHash32()
        : this(0)
    {
    }

    /// <summary>
    ///   初始化 <see cref="XxHash32"/> 类的新实例
    ///   指定的种子。
    /// </summary>
    /// <param name="seed">
    ///   用于计算此实例的散列种子值 。
    /// </param>
    public XxHash32(int seed)
        : base(HashSize)
    {
        _seed = (uint)seed;
        Reset();
    }

    /// <summary>
    ///   将散列计算重置到初始状态。
    /// </summary>
    public override void Reset()
    {
        _state = new State(_seed);
        _length = 0;
    }

    /// <summary>
    ///   将 <paramref name="source"/> 的内容附加到已经的数据中
    ///   用于当前散列计算处理。
    /// </summary>
    /// <param name="source">要处理的数据 。</param>
    public override void Append(ReadOnlySpan<byte> source)
    {
        // 每次我们读16字节 处理条纹
        // 数据不是完全的MT -16 被储存在一个缓存状态
        // 缓冲。

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
    ///   将计算成散列值写入 <paramref name="destination"/>
    ///   但不修改累积状态。
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
    ///   计算所提供数据的 XxHash32 散列。
    /// </summary>
    /// <param name="source">类组数据。</param>
    /// <returns>下 XxHash32 提供的数据的散数。</returns>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="source"/> 是 <see langword="null"/>。
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
    ///   使用所提供的种子计算所提供数据的 XxHash32 散列。
    /// </summary>
    /// <param name="source">类组数据。</param>
    /// <param name="seed">用于计算散列的种子值。</param>
    /// <returns>下 XxHash32 提供的数据的散数。</returns>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="source"/> 是 <see langword="null"/>。
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
    ///   计算所提供数据的 XxHash32 散列。
    /// </summary>
    /// <param name="source">类组数据。</param>
    /// <param name="seed">此散列计算所用的种子值。 默认值为零 。</param>
    /// <returns>下 XxHash32 提供的数据的散数。</returns>
    public static byte[] Hash(ReadOnlySpan<byte> source, int seed = 0)
    {
        byte[] ret = new byte[HashSize];
        StaticHash(source, ret, seed);
        return ret;
    }

    /// <summary>
    ///   试图将所提供的数据 XxHash32 散列计算成提供的目标。
    /// </summary>
    /// <param name="source">类组数据。</param>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <param name="bytesWritten">
    ///   成功时, 收到写到 <paramref name="destination"/>的字节数 。
    /// </param>
    /// <param name="seed">此散列计算所用的种子值。 默认值为零 。</param>
    /// <returns>
    ///   <see langword="true"/> 如果 <paramref name="destination"/> 足够长的时间来接收
    ///   计算散列值(4字节);否则, <see langword="false"/>。
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
    ///   将所提供的数据 XxHash32 散列计算成所提供目的地。
    /// </summary>
    /// <param name="source">类组数据。</param>
    /// <param name="destination">接收计算散列值的缓冲。</param>
    /// <param name="seed">此散列计算所用的种子值。 默认值为零 。</param>
    /// <returns>
    ///   写入 <paramref name="destination"/>的字节数。
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
