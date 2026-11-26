using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Collections;

#if NET6_0_OR_GREATER
using System.Numerics;
#else
using Orleans.Serialization.Utilities;
#endif

namespace Orleans.Serialization.Buffers;

/// <summary>
/// <see cref="IBufferWriter{T}"/> 实施使用专门用来创建 <see cref="ReadOnlySequence{T}"/> 实例的集合阵列。
/// </summary>
/// <remarks>
/// 与 <see cref="ArrayPool{T}.Shared"/>一样,后背缓冲带可包含先前用途和
/// 不经清除返回后被返回。调用者必须在推进写作器之前先初始化每个字节
/// 仅读取缓冲文字区域。
/// 根据最近的池活动,固定页数被重新使用和每10秒钟将页面拆迁一次。
/// 当上一个拥有者释放时,较大的阵列返回 <see cref="ArrayPool{T}.Shared"/> 。
/// </remarks>
[StructLayout(LayoutKind.Auto)]
[Immutable]
public sealed class ArcBufferWriter : IBufferWriter<byte>, IDisposable
{
    // 第一页。 这是消费者消费的页面 。
    // 这可以等于当前页,也可以等于上页。
    private ArcBufferPage _readPage;

    // 当前页面。 这是下个写入时要写入的页面 。
    private ArcBufferPage _writePage;

    // 当前页面。 这是所分配的最后一页 。 在由页面组成的链接列表中, _ first _\ _ put {% {} _ tild 。
    private ArcBufferPage _tail;

    // 已经消耗的第一张页的偏移值。 当此页到达页面末尾时, 可以将页面拆开 。
    private int _readIndex;

    // 缓冲总长度 。
    private int _totalLength;

    // 皮条页可以读和处理,但不得变换。
    private bool _hasPinnedPages;

    /// <summary>
    /// 获得最小页码大小 。
    /// </summary>
    public const int MinimumPageSize = ArcBufferPagePool.MinimumPageSize;

    /// <summary>
    /// 初始化 <see cref="ArcBufferWriter"/> 结构的新实例。
    /// </summary>
    public ArcBufferWriter()
    {
        _readPage = _writePage = _tail = ArcBufferPagePool.Shared.Rent();
        Debug.Assert(_readPage.ReferenceCount == 0);
        _readPage.Pin(_readPage.Version);
    }

    /// <summary>
    /// 获取未消耗字节数 。
    /// </summary>
    public int UnconsumedLength => _totalLength - _readIndex;

    /// <summary>
    /// 获取未消耗的字节数( 用于 UnconsumedLength).
    /// </summary>
    public int Length => UnconsumedLength;

    /// <summary>
    /// 获得一个读者对未消耗字节的读取。
    /// </summary>
    public ArcBufferReader Reader => new(this);

    /// <summary>
    /// 在目的地列表达到其容量之前添加额外的缓冲文件。
    /// </summary>
    /// <param name="buffers">添加缓冲的目的地。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReplenishBuffers(List<ArraySegment<byte>> buffers)
    {
        ThrowIfPinnedPages();

        // 跳过半满页的页面,以尽量减少添加到目的地的缓冲量
        // 这样做可以调整,以增加页数利用率。
        const int MinimumUsablePageSize = MinimumPageSize / 2;

        while (buffers.Count < buffers.Capacity)
        {
            // 仅在当前页面超过最小“可用”页面大小时使用此页面。
            if (_tail.WriteCapacity > MinimumUsablePageSize)
            {
                buffers.Add(_tail.WritableArraySegment);
            }

            // 分配新页面。
            AllocatePage(0);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IBufferWriter<byte>.Advance(int count) => AdvanceWriter(count);

    /// <summary>
    /// 以指定的字节数将写作器预发。
    /// </summary>
    /// <param name="count">用于推进作家的字节数。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AdvanceWriter(int count)
    {
        ThrowIfPinnedPages();

        _totalLength += count;
        while (true)
        {
            var amount = Math.Min(_writePage.WriteCapacity, count);
            _writePage.Advance(amount);
            count -= amount;

            if (count == 0)
            {
                break;
            }

            var next = _writePage.Next;
            Debug.Assert(next is not null);
            _writePage = next;
        }
    }

    /// <summary>
    /// 重设此实例, 返回全部内存 。
    /// </summary>
    public void Reset() => Reset(0);

    internal void Reset(int minimumPageSize)
    {
        if (minimumPageSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPageSize));
        }

        UnpinAll();
        _totalLength = _readIndex = 0;
        _readPage = _writePage = _tail = ArcBufferPagePool.Shared.Rent(minimumPageSize);
        _hasPinnedPages = false;
        Debug.Assert(_readPage.ReferenceCount == 0);
        _readPage.Pin(_readPage.Version);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        UnpinAll();
        _totalLength = _readIndex = 0;
        _readPage = _writePage = _tail = null!;
        _hasPinnedPages = false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ThrowIfPinnedPages();

        if (sizeHint >= _writePage.WriteCapacity)
        {
            return GetMemorySlow(sizeHint);
        }

        return _writePage.WritableMemory;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ThrowIfPinnedPages();

        if (sizeHint >= _writePage.WriteCapacity)
        {
            return GetSpanSlow(sizeHint);
        }

        return _writePage.WritableSpan;
    }

    /// <summary>
    /// 试图读取缓冲区内提供的字节数。
    /// </summary>
    /// <param name="destination">目的地,如果数据需要复制,可用以保存所请求的数据。</param>
    /// <returns>如果数据不存在,则以零长度为空格,如果有数据可用,至少是要求的长度。</returns>
    public ReadOnlySpan<byte> Peek(scoped in Span<byte> destination)
    {
        if (destination.Length > UnconsumedLength)
        {
            return [];
        }

        // 单间距
        var firstSpan = _readPage.AsSpan(_readIndex, Math.Min(_readPage.Length - _readIndex, UnconsumedLength));
        if (firstSpan.Length >= destination.Length)
        {
            return firstSpan;
        }

        // 多长的跨度。 创建一块切片, 不钉它, 因为我们会立即打开它。
        Peek(destination);
        return destination;
    }

    /// <summary>将此作家的内容复制到一个字符串。</summary>
    /// <remarks>此方法不推进读取光标。</remarks>
    public int Peek(Span<byte> output)
    {
        var bytesCopied = 0;
        var remaining = UnconsumedLength;
        var current = _readPage;
        var offset = _readIndex;
        while (output.Length > 0 && remaining > 0 && current != null)
        {
            var segment = current.AsSpan(offset, current.Length - offset);
            var copyLength = Math.Min(Math.Min(segment.Length, output.Length), remaining);
            bytesCopied += copyLength;
            var slice = segment[..copyLength];
            slice.CopyTo(output);
            output = output[slice.Length..];
            remaining -= copyLength;
            current = current.Next;
            offset = 0;
        }

        return bytesCopied;
    }

    /// <summary>
    /// 将提供的序列写入此缓冲。
    /// </summary>
    /// <param name="input">要写入的数据。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySequence<byte> input)
    {
        ThrowIfPinnedPages();

        foreach (var segment in input)
        {
            Write(segment.Span);
        }
    }

    /// <summary>
    /// 将提供的值写入此缓冲。
    /// </summary>
    /// <param name="value">要写入的数据。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> value)
    {
        ThrowIfPinnedPages();

        var destination = GetSpan();

        // 快路径, 尝试直接复制到可用内存
        if (value.Length <= destination.Length)
        {
            value.CopyTo(destination);
            AdvanceWriter(value.Length);
        }
        else
        {
            WriteMultiSegment(value, destination);
        }
    }

    private void WriteMultiSegment(in ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var input = source;
        while (true)
        {
            var writeSize = Math.Min(destination.Length, input.Length);
            input[..writeSize].CopyTo(destination);
            AdvanceWriter(writeSize);
            input = input[writeSize..];
            if (input.Length > 0)
            {
                destination = GetSpan();
                continue;
            }

            return;
        }
    }

    /// <summary>
    /// 覆盖已经写入但不会改变读者或写入位置的字节。
    /// </summary>
    public void WriteAt(int offset, ReadOnlySpan<byte> value)
    {
        ThrowIfPinnedPages();
        if (offset < 0 || offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (value.Length > Length - offset) throw new ArgumentOutOfRangeException(nameof(value));
        if (value.IsEmpty) return;

        var page = _readPage;
        var pageOffset = _readIndex + offset;
        while (pageOffset >= page.Length)
        {
            pageOffset -= page.Length;
            page = page.Next!;
        }

        while (!value.IsEmpty)
        {
            var count = Math.Min(value.Length, page.Length - pageOffset);
            value[..count].CopyTo(page.AsSpan(pageOffset, count));
            value = value[count..];
            page = page.Next!;
            pageOffset = 0;
        }
    }

    /// <summary>
    /// 将此缓冲线切换到指定的未消耗长度。
    /// </summary>
    public void Truncate(int length)
    {
        ThrowIfPinnedPages();
        if (length < 0 || length > Length) throw new ArgumentOutOfRangeException(nameof(length));

        if (length == Length)
        {
            return;
        }

        var remaining = _readIndex + length;
        var page = _readPage;
        while (remaining > page.Length)
        {
            remaining -= page.Length;
            page = page.Next!;
        }

        page.SetLength(remaining, page.Version);
        var discarded = page.Next;
        page.ClearNext(page.Version);
        while (discarded is not null)
        {
            var next = discarded.Next;
            discarded.Unpin(discarded.Version);
            discarded = next;
        }

        _writePage = _tail = page;
        _totalLength = _readIndex + length;
    }

    /// <summary>
    /// 以插针方式附加 <paramref name="input"/> 所引用的页面。
    /// </summary>
    /// <exception cref="ObjectDisposedException">作家已处理。</exception>
    public void AppendPinned(ArcBuffer input)
    {
        if (input.Length == 0)
        {
            ThrowIfDisposed();
            return;
        }

        ThrowIfPinnedPages();
        if (_totalLength != 0 || _readIndex != 0 || _readPage != _writePage || _writePage != _tail || _readPage.Length != 0)
        {
            throw new InvalidOperationException("Pinned pages can only be appended to an empty writer.");
        }

        input.Pin();

        ArcBufferPage? tail = null;
        foreach (var segment in input.PageSegments)
        {
            tail = segment.Page;
        }

        _readPage.Unpin(_readPage.Version);
        _readPage = input.First!;
        _writePage = _tail = tail!;
        _readIndex = input.Offset;
        _totalLength = checked(input.Offset + input.Length);
        _hasPinnedPages = true;
    }

    private void ThrowIfPinnedPages()
    {
        ThrowIfDisposed();

        if (_hasPinnedPages)
        {
            throw new InvalidOperationException("A writer containing pinned pages cannot be mutated.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_writePage is null)
        {
            throw new ObjectDisposedException(nameof(ArcBufferWriter));
        }
    }

    internal bool TryPeek(long offset, out byte value)
    {
        ThrowIfDisposed();

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var length = Length;
        if (offset >= length)
        {
            value = default;
            return false;
        }

        // 拥有的缓冲文件以写入页面书面长度结束; 折叠片可结束一个页的半径。
        if (!_hasPinnedPages)
        {
            var writePageOffset = offset - (length - _writePage.Length);
            if (writePageOffset >= 0)
            {
                value = _writePage.AsSpan((int)writePageOffset, 1)[0];
                return true;
            }
        }

        var page = _readPage;
        var pageOffset = _readIndex + checked((int)offset);
        while (pageOffset >= page.Length)
        {
            pageOffset -= page.Length;
            page = page.Next!;
        }

        value = page.Array[pageOffset];
        return true;
    }

    internal long IndexOf(byte value)
    {
        for (long i = 0; i < Length; i++)
        {
            if (TryPeek(i, out var candidate) && candidate == value)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 解开所有页面 。
    /// </summary>
    private void UnpinAll()
    {
        var current = _readPage;
        while (current != null)
        {
            var previous = current;
            current = ReferenceEquals(previous, _tail) ? null : previous.Next;
            previous.Unpin(previous.Version);
        }
    }

    /// <summary>
    /// 返回提供长度的片段,但不标明所称消耗的数据。
    /// </summary>
    /// <param name="count">要消耗的字节数。</param>
    /// <returns>一片未消耗的数据</returns>
    /// <exception cref="ObjectDisposedException">作家已处理。</exception>
    public ArcBuffer PeekSlice(int count)
    {
        ThrowIfDisposed();

#if NET6_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, UnconsumedLength);
#else
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Length must be greater than or equal to 0.");
        if (count > UnconsumedLength) throw new ArgumentOutOfRangeException(nameof(count), "Length must be less than or equal to the unconsumed length of the buffer.");
#endif
        Debug.Assert(count >= 0);
        Debug.Assert(count <= UnconsumedLength);

        var result = new ArcBuffer(_readPage, _readIndex, count);
        result.Pin();
        return result;
    }

    /// <summary>
    /// 消耗了规定长度的一小部分。
    /// </summary>
    /// <param name="count">要消耗的字节数。</param>
    /// <returns>代表消耗数据的缓冲符。</returns>
    public ArcBuffer ConsumeSlice(int count)
    {
        var result = PeekSlice(count);

        // 向光标前进,以便随后的切片呼叫返回下一个切片。
        AdvanceReader(count);

        return result;
    }

    /// <summary>
    /// 以指定的字节数向读者预发。
    /// </summary>
    /// <param name="count">预发读者的字节数。</param>
    /// <exception cref="ObjectDisposedException">作家已处理。</exception>
    public void AdvanceReader(int count)
    {
        ThrowIfDisposed();

#if NET6_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, UnconsumedLength);
#else
        if (count < 0 || count > UnconsumedLength) throw new ArgumentOutOfRangeException(nameof(count));
#endif

        _readIndex += count;

        // 如果此电话会消耗整个第一页, 而该页面不是当前写入的页面, 请打开它 。
        while (_readIndex >= _readPage.Length && _writePage != _readPage)
        {
            // 推进消耗的长度。
            var current = _readPage;
            _readIndex -= current.Length;
            _totalLength -= current.Length;

            // 向下一页预发
            Debug.Assert(current.Next is not null);
            _readPage = current.Next!;

            // 解开页面
            current.Unpin(current.Version);
        }

        Debug.Assert(_readPage is not null);
        Debug.Assert(_readIndex <= _readPage.Length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Span<byte> GetSpanSlow(int sizeHint) => AdvanceWritePage(sizeHint).Array;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Memory<byte> GetMemorySlow(int sizeHint) => AdvanceWritePage(sizeHint).AsMemory(0);

    private ArcBufferPage AllocatePage(int sizeHint)
    {
        Debug.Assert(_tail.Next is null);

        var newBuffer = ArcBufferPagePool.Shared.Rent(sizeHint);
        Debug.Assert(newBuffer.ReferenceCount == 0);
        newBuffer.Pin(newBuffer.Version);
        _tail.SetNext(newBuffer, _tail.Version);
        return _tail = newBuffer;
    }

    private ArcBufferPage AdvanceWritePage(int sizeHint)
    {
        var next = _writePage.Next;
        if (next is null)
        {
            next = AllocatePage(sizeHint);
        }

        _writePage = next;
        return next;
    }
}

internal sealed class ArcBufferPagePool
{
    public static ArcBufferPagePool Shared { get; } = new();
    public const int MinimumPageSize = 16 * 1024;
    private static readonly Timer _evictionTimer = CreateEvictionTimer();
    private readonly object _evictionLock = new();
    private readonly ConcurrentQueue<ArcBufferPage> _pages = new();
    private long _rentCount;
    private long _returnCount;

    internal int RetainedPages => _pages.Count;

    public ArcBufferPage Rent(int size = -1)
    {
        if (size > MinimumPageSize)
        {
            return new ArcBufferPage(size);
        }

        if (_pages.TryDequeue(out var block))
        {
            Interlocked.Increment(ref _rentCount);
            return block;
        }

        // 误差表示需求增加。 重两倍以避免在分配周期性断裂期间将网页拆迁。
        Interlocked.Add(ref _rentCount, 2);
        return new ArcBufferPage(size);
    }

    internal void Return(ArcBufferPage block)
    {
        Debug.Assert(block.IsValid);
        Debug.Assert(block.ReferenceCount == 0);
        if (block.IsMinimumSize)
        {
            Interlocked.Increment(ref _returnCount);
            _pages.Enqueue(block);
        }
        else
        {
            block.ReleaseArray();
        }
    }

    internal void Evict()
    {
        lock (_evictionLock)
        {
            var rents = Interlocked.Exchange(ref _rentCount, 0);
            var returns = Interlocked.Exchange(ref _returnCount, 0);
            var count = _pages.Count;
            // 保护不断增长的需求, 逐步使用超量缓慢,
            // 并更快地恢复闲置能力。 使用此选项计算租金/ 返回赛跑时, 算得差不多 。
            var releaseCount = 0;
            if (rents == 0 && returns == 0)
            {
                releaseCount = Math.Max(10, count / 20);
            }
            else if (returns > rents)
            {
                releaseCount = (int)Math.Min(count / 100, (returns - rents) / 5);
            }
            else if (returns == rents)
            {
                releaseCount = Math.Max(1, count / 100);
            }

            while (releaseCount-- > 0 && TryReleasePage())
            {
            }
        }
    }

    private bool TryReleasePage()
    {
        if (!_pages.TryDequeue(out var block))
        {
            return false;
        }

        block.ReleaseArray();
        return true;
    }

    private static Timer CreateEvictionTimer()
    {
        // 整个过程的定时器不得保留第一个缓冲用户的执行环境。
        if (ExecutionContext.IsFlowSuppressed())
        {
            return CreateTimer();
        }

        using (ExecutionContext.SuppressFlow())
        {
            return CreateTimer();
        }

        static Timer CreateTimer() => new(static state => ((ArcBufferPagePool)state!).Evict(), Shared, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }
}

/// <summary>
/// 数据页。
/// </summary>
public sealed class ArcBufferPage
{
    // 页面当前版本。 每次页面返回集合时,都会加插。
    // 这有助于确保页面在返回池子后不消耗。
    // 这是防范某些程序错误的戒备
    private int _version;

    // 当前引用计数 。 此选项用于确保页面在使用中不会返回到集合中 。
    private int _refCount;

    internal ArcBufferPage()
    {
        Array = [];
    }

    internal ArcBufferPage(int length)
    {
#if !NET6_0_OR_GREATER
        Array = null!;
#endif
        InitializeArray(length);
    }

    /// <summary>
    /// 调整后台阵列大小以容纳大段。
    /// </summary>
    /// <param name="length">最低所需长度,以字节计。</param>
    public void ResizeLargeSegment(int length)
    {
        Debug.Assert(length > ArcBufferPagePool.MinimumPageSize);
        InitializeArray(length);
    }

#if NET6_0_OR_GREATER
    [MemberNotNull(nameof(Array))]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitializeArray(int length)
    {
        if (length <= ArcBufferPagePool.MinimumPageSize)
        {
            Debug.Assert(Array is null);
#if NET6_0_OR_GREATER
            var array = GC.AllocateUninitializedArray<byte>(ArcBufferPagePool.MinimumPageSize, pinned: true);
#else
                var array = new byte[ArcBufferPagePool.MinimumPageSize];
#endif
            Array = array;
        }
        else
        {
            // 集中到两个力量
            length = (int)BitOperations.RoundUpToPowerOf2((uint)length);

            if (Array is not null)
            {
                // 段的大小已经适当。
                if (Array.Length == length)
                {
                    return;
                }

                // 正在调整该段的大小。
                ArrayPool<byte>.Shared.Return(Array);
            }

            Array = ArrayPool<byte>.Shared.Rent(length);
        }
    }

    internal void ReleaseArray()
    {
        if (!IsMinimumSize)
        {
            ArrayPool<byte>.Shared.Return(Array);
        }

        Array = [];
    }

    /// <summary>
    /// 获取页面的矩阵。
    /// </summary>
    public byte[] Array { get; private set; }

    /// <summary>
    /// 获取已写入页面的字节数 。
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// A <see cref="ReadOnlySpan{T}"/> 包含本页可读字节。
    /// </summary>
    public ReadOnlySpan<byte> ReadableSpan => Array.AsSpan(0, Length);

    /// <summary>
    /// A <see cref="ReadOnlyMemory{T}"/> 包含本页可读字节。
    /// </summary>
    public ReadOnlyMemory<byte> ReadableMemory => AsMemory(0, Length);

    /// <summary>
    /// 包含此页可读字节的 <see cref="ArraySegment{T}"/> 。
    /// </summary>
    public ArraySegment<byte> ReadableArraySegment => new(Array, 0, Length);

    /// <summary>
    /// 包含本页可写字节的 <see cref="ArraySegment{T}"/> 。
    /// </summary>
    public ArraySegment<byte> WritableArraySegment => new(Array, Length, Array.Length - Length);

    /// <summary>
    /// 获得下一个节点。
    /// </summary>
    public ArcBufferPage? Next { get; private set; }

    /// <summary>
    /// 获取当前页面版本。
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// 获得一个值,以显示该页面是否有效。
    /// </summary>
    public bool IsValid => Array is { Length: > 0 };

    /// <summary>
    /// 获得一个值,以显示该页面是否等于最小页码。
    /// </summary>
    public bool IsMinimumSize => Array.Length == ArcBufferPagePool.MinimumPageSize;

    /// <summary>
    /// 获取页面中可用于写入的字节数。
    /// </summary>
    public int WriteCapacity => Array.Length - Length;

    /// <summary>
    /// 页面中包含可写内存。
    /// </summary>
    public Memory<byte> WritableMemory => AsMemory(Length);

    /// <summary>
    /// 获得一个代表页面中可写内存的宽度。
    /// </summary>
    public Span<byte> WritableSpan => AsSpan(Length);

    /// <summary>
    /// 获取当前页面参考编号。
    /// </summary>
    internal int ReferenceCount => _refCount;

    /// <summary>
    /// 在目标页面中从指定位置开始的部分上创建新的内存区域。
    /// </summary>
    /// <param name="offset">将数组中的偏移值转换为返回内存。</param>
    /// <returns>内存区域。</returns>
    public Memory<byte> AsMemory(int offset)
    {
#if NET6_0_OR_GREATER
        if (IsMinimumSize)
        {
            return MemoryMarshal.CreateFromPinnedArray(Array, offset, Array.Length - offset);
        }
#endif

        return Array.AsMemory(offset);
    }

    /// <summary>
    /// 在目标页面中从指定位置开始的部分创建新的内存区域,该位置有指定的长度。
    /// </summary>
    /// <param name="offset">内存区域从数组中开始的偏移。</param>
    /// <param name="length">内存区域的长度。</param>
    /// <returns>内存区域。</returns>
    public Memory<byte> AsMemory(int offset, int length)
    {
#if NET6_0_OR_GREATER
        if (IsMinimumSize)
        {
            return MemoryMarshal.CreateFromPinnedArray(Array, offset, length);
        }
#endif

        return Array.AsMemory(offset, length);
    }

    /// <summary>
    /// 返回一个直指底部数组的数组段,从提供的偏移开始,并具有所提供的长度。
    /// </summary>
    /// <param name="offset">数组段起始数组的偏移。</param>
    /// <param name="length">数组段的长度。</param>
    /// <returns>数组段。</returns>
    public ArraySegment<byte> AsArraySegment(int offset, int length) => new(Array, offset, length);

    /// <summary>
    /// 从提供的偏移开始, 以底部数组为指向的宽度。
    /// </summary>
    /// <param name="offset">转自</param>
    /// <returns>宽度</returns>
    public Span<byte> AsSpan(int offset) => Array.AsSpan(offset);

    /// <summary>
    /// 从提供的偏移开始, 以底部数组为指向的宽度。
    /// </summary>
    /// <param name="offset">转自</param>
    /// <param name="length">长度。</param>
    /// <returns>宽度</returns>
    public Span<byte> AsSpan(int offset, int length) => Array.AsSpan(offset, length);

    /// <summary>
    /// 将页面上写入的字节数增加提供的数额。
    /// </summary>
    /// <param name="bytes">将本页长度增加的字节数。</param>
    public void Advance(int bytes)
    {
        Length += bytes;
        Debug.Assert(Length <= Array.Length);
    }

    /// <summary>
    /// 设置序列中的下一页。
    /// </summary>
    /// <param name="next">序列中的下一页。</param>
    /// <param name="token">标记, 此操作要被允许, 必须与页面的 <see cref="Version"/> 匹配。</param>
    public void SetNext(ArcBufferPage next, int token)
    {
        Debug.Assert(Next is null);
        CheckValidity(token);
        Next = next;
    }

    internal void SetLength(int length, int token)
    {
        CheckValidity(token);
        if (length < 0 || length > Array.Length) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
    }

    internal void ClearNext(int token)
    {
        CheckValidity(token);
        Next = null;
    }

    /// <summary>
    /// 防止将它送回到页面池。
    /// </summary>
    /// <param name="token">标记, 此操作要被允许, 必须与页面的 <see cref="Version"/> 匹配。</param>
    public void Pin(int token)
    {
        if (token != _version)
        {
            ThrowInvalidVersion();
        }

        Interlocked.Increment(ref _refCount);
    }

    /// <summary>
    /// 将此页面解开, 允许将其返回到页面池中 。
    /// </summary>
    /// <param name="token">标记, 此操作要被允许, 必须与页面的 <see cref="Version"/> 匹配。</param>
    public void Unpin(int token)
    {
        CheckValidity(token);
        if (Interlocked.Decrement(ref _refCount) == 0)
        {
            Return();
        }
    }

    private void Return()
    {
        Debug.Assert(_refCount == 0);
        Length = 0;
        Next = default;
        Interlocked.Increment(ref _version);
        ArcBufferPagePool.Shared.Return(this);
    }

    /// <summary>
    /// 如果提供的 <paramref name="token"/> 与页面的 <see cref="Version"/>不符,则投递。
    /// </summary>
    /// <param name="token">标记, 必须与页面 <see cref="Version"/>相匹配。</param>
    public void CheckValidity(int token)
    {
        if (token != _version)
        {
            ThrowInvalidVersion();
        }

        if (_refCount <= 0)
        {
            ThrowAccessViolation();
        }
    }

    [DoesNotReturn]
    private static void ThrowInvalidVersion() => throw new InvalidOperationException("An invalid token was provided when attempting to perform an operation on this page.");

    [DoesNotReturn]
    private static void ThrowAccessViolation() => throw new InvalidOperationException("An attempt was made to access a page with an invalid reference count.");
}

/// <summary>
/// 提供读者访问 <see cref="ArcBufferWriter"/>的机会。
/// </summary>
/// <param name="writer">作家。</param>
public readonly struct ArcBufferReader(ArcBufferWriter writer)
{
    /// <summary>
    /// 获取未消耗字节数 。
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => writer.UnconsumedLength;
    }

    /// <summary>
    /// 试图读取缓冲区内提供的字节数。
    /// </summary>
    /// <param name="destination">目的地,如果数据需要复制,可用以保存所请求的数据。</param>
    /// <returns>如果数据不可用,则长度为零,如果数据可用,则长度为零;如果数据可用,则长度为要求的长度。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> Peek(scoped in Span<byte> destination) => writer.Peek(in destination);

    /// <summary>
    /// 以指定偏移点的一个字节的坡脚进入未读数据中。
    /// </summary>
    public byte Peek(long offset)
    {
        if (!writer.TryPeek(offset, out var value))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return value;
    }

    /// <summary>
    /// 返回提供长度的片段,但不标明所称消耗的数据。
    /// </summary>
    /// <param name="count">要消耗的字节数。</param>
    /// <returns>一片未消耗的数据</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArcBuffer PeekSlice(int count) => writer.PeekSlice(count);

    /// <summary>
    /// 消耗了规定长度的一小部分。
    /// </summary>
    /// <param name="count">要消耗的字节数。</param>
    /// <returns>代表消耗数据的缓冲符。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArcBuffer ConsumeSlice(int count) => writer.ConsumeSlice(count);

    /// <summary>
    /// 读取到下次发生 <paramref name="delimiter"/>时的状态。
    /// </summary>
    public bool TryReadTo(out ArcBuffer slice, byte delimiter, bool advancePastDelimiter = true)
    {
        var index = writer.IndexOf(delimiter);
        if (index < 0)
        {
            slice = default;
            return false;
        }

        slice = writer.ConsumeSlice(checked((int)index));
        if (advancePastDelimiter)
        {
            writer.AdvanceReader(1);
        }

        return true;
    }

    /// <summary>
    /// 检查下个字节是否匹配 <paramref name="next"/>。
    /// </summary>
    public bool IsNext(ReadOnlySpan<byte> next, bool advancePast = false)
    {
        if (next.Length > writer.Length)
        {
            return false;
        }

        for (var i = 0; i < next.Length; i++)
        {
            if (!writer.TryPeek(i, out var value) || value != next[i])
            {
                return false;
            }
        }

        if (advancePast)
        {
            writer.AdvanceReader(next.Length);
        }

        return true;
    }

    /// <summary>
    /// 计算跨度内的数据数量。
    /// </summary>
    /// <param name="output"></param>
    public void Consume(Span<byte> output)
    {
        var count = writer.Peek(output);
        if (count != output.Length)
        {
            throw new InvalidOperationException("Attempted to consume more data than is available.");
        }

        writer.AdvanceReader(count);
    }

    public void Skip(int count)
    {
        writer.AdvanceReader(count);
    }
}

/// <summary>
/// 表示从 <see cref="ArcBufferWriter"/>中取出的一个参考点算的粗字节片。
/// </summary>
/// <remarks>
/// 无需处置时必须处理自有切片。 复制此支架可创建借出视图
/// 使用 <see cref="Slice(int)"/> 获得独立所有人。
/// 在向另一个同时拥有者发放缓冲之前,获得独立所有权。
/// 每个业主都有自己的结构副本,并准确处理一次;各页一直固定,直到所有业主都放行。
/// 引用的字节必须在使用切片时保持不变。
/// 默认值是一个有效的无自有空缓冲。
/// </remarks>
public struct ArcBuffer : IDisposable
{
    /// <summary>
    /// 创建对有效、 被钉定的页面链的借阅视图。
    /// </summary>
    /// <param name="first">第一个页面,或无自有空缓冲的 <see langword="null"/> 。</param>
    /// <param name="token">当前 <paramref name="first"/>的标识。</param>
    /// <param name="offset">抵销数 <paramref name="first"/> 开始于此视图的起点 。</param>
    /// <param name="length">视图覆盖的字节数。</param>
    /// <remarks>
    /// 使用该视图时, 拥有的缓冲器必须保持引用页固定不变, 字节不变 。
    /// 要求 <see cref="Pin"/> 获得独立所有权。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 下页首页外的加号是负或超过链接页。
    /// 或无自有缓冲带的长度为非零抵消或零误。
    /// </exception>
    /// <exception cref="InvalidOperationException">页面符号或引用编号无效。</exception>
    public ArcBuffer(ArcBufferPage? first, int token, int offset, int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (offset < 0 || offset > (first?.Length ?? 0)) throw new ArgumentOutOfRangeException(nameof(offset));
        if (first is null)
        {
            if (length != 0) throw new ArgumentOutOfRangeException(nameof(length));
        }
        else
        {
            first.CheckValidity(token);
            var remaining = length - (first.Length - offset);
            var page = first;
            while (remaining > 0)
            {
                page = page.Next;
                if (page is null) throw new ArgumentOutOfRangeException(nameof(length), "The length exceeds the bytes in the linked pages.");
                page.CheckValidity(page.Version);
                remaining -= page.Length;
            }
        }

        First = first;
        _firstPageToken = token;
        Offset = offset;
        Length = length;
    }

    // 作家和切片操作设置界限,并固定参考页。
    internal ArcBuffer(ArcBufferPage first, int offset, int length)
    {
        Debug.Assert(offset >= 0 && offset <= first.Length);
        Debug.Assert(length >= 0);
        First = first;
        _firstPageToken = first.Version;
        Offset = offset;
        Length = length;
    }

    /// <summary>
    /// 获得此片段指向的第一页的标记。
    /// </summary>
    private int _firstPageToken;

    /// <summary>
    /// 获取第一个页面,或者无自有空缓冲的 <see langword="null"/> 。
    /// </summary>
    public readonly ArcBufferPage? First;

    /// <summary>
    /// 将偏移移到此切片起始的第一页。
    /// </summary>
    public readonly int Offset;

    /// <summary>
    /// 获取此序列的长度 。
    /// </summary>
    public readonly int Length;

    /// <summary>获得一个空的没有页面的缓冲。</summary>
    public static ArcBuffer Empty => default;

    /// <summary>获取是否此缓冲包含无字节。</summary>
    public readonly bool IsEmpty => Length == 0;

    /// <summary>将此作家的内容复制到一个字符串。</summary>
    public readonly int CopyTo(Span<byte> output)
    {
        CheckValidity();
        var copied = 0;
        foreach (var span in this)
        {
            var slice = span[..Math.Min(span.Length, output.Length)];
            slice.CopyTo(output);
            output = output[slice.Length..];
            copied += slice.Length;
        }

        return copied;
    }

    /// <summary>将此作家的内容复制到集合缓冲。</summary>
    public readonly void CopyTo(ArcBufferWriter output)
    {
        CheckValidity();
        foreach (var span in this)
        {
            output.Write(span);
        }
    }

    /// <summary>将此作家的内容复制到缓冲作者。</summary>
    public readonly void CopyTo<TBufferWriter>(ref TBufferWriter output) where TBufferWriter : IBufferWriter<byte>
    {
        CheckValidity();
        foreach (var span in this)
        {
            Write(ref output, span);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write<TBufferWriter>(ref TBufferWriter writer, ReadOnlySpan<byte> value) where TBufferWriter : IBufferWriter<byte>
    {
        var destination = writer.GetSpan();

        // 快路径, 尝试直接复制到可用内存
        if (value.Length <= destination.Length)
        {
            value.CopyTo(destination);
            writer.Advance(value.Length);
        }
        else
        {
            WriteMultiSegment(ref writer, value, destination);
        }
    }

    private static void WriteMultiSegment<TBufferWriter>(ref TBufferWriter writer, in ReadOnlySpan<byte> source, Span<byte> destination) where TBufferWriter : IBufferWriter<byte>
    {
        var input = source;
        while (true)
        {
            var writeSize = Math.Min(destination.Length, input.Length);
            input[..writeSize].CopyTo(destination);
            writer.Advance(writeSize);
            input = input[writeSize..];
            if (input.Length > 0)
            {
                destination = writer.GetSpan();

                if (destination.IsEmpty)
                {
                    ThrowInsufficientSpaceException();
                }

                continue;
            }

            return;
        }
    }

    [DoesNotReturn]
    private static void ThrowInsufficientSpaceException() => throw new InvalidOperationException("Insufficient capacity in provided buffer");

    /// <summary>
    /// 返回新的 <see cref="ReadOnlySequence{T}"/> ,在处理此实例后不得访问。
    /// </summary>
    public readonly ReadOnlySequence<byte> AsReadOnlySequence()
    {
        var runningIndex = 0L;
        ReadOnlySequenceSegment? first = null;
        ReadOnlySequenceSegment? previous = null;
        var endIndex = 0;
        foreach (var memory in MemorySegments)
        {
            var current = new ReadOnlySequenceSegment(memory, runningIndex);
            first ??= current;
            endIndex = memory.Length;
            runningIndex += endIndex;
            previous?.SetNext(current);
            previous = current;
        }

        if (first is null)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        Debug.Assert(previous is not null);
        if (previous == first)
        {
            return new ReadOnlySequence<byte>(first.Memory);
        }

        return new ReadOnlySequence<byte>(first, 0, previous, endIndex);
    }

    /// <summary>
    /// 返回以数组形式写入的数据。
    /// </summary>
    /// <returns>已经写入的数据 。</returns>
    public readonly byte[] ToArray()
    {
        CheckValidity();
        var result = new byte[Length];
        CopyTo(result);
        return result;
    }

    /// <summary>
    /// 如果缓冲符不再有效, 则丢弃 。
    /// </summary>
    internal readonly void CheckValidity()
    {
        if (First is not null)
        {
            First.CheckValidity(_firstPageToken);
        }
        else if (Length != 0 || Offset != 0)
        {
            throw new InvalidOperationException("An owner-free buffer must be empty.");
        }
    }

    /// <summary>
    /// 从指定的偏移到此缓冲的结尾,创建一个切片。
    /// </summary>
    /// <param name="offset">切片开始时的零基偏移。</param>
    /// <returns>一块在不再需要时必须处置的切片。</returns>
    public readonly ArcBuffer Slice(int offset) => Slice(offset, Length - offset);

    /// <summary>
    /// 创建指定长度的片段。
    /// </summary>
    /// <param name="offset">切片开始时的零基偏移。</param>
    /// <param name="length">切片的长度。</param>
    /// <returns>一块在不再需要时必须处置的切片。</returns>
    public readonly ArcBuffer Slice(int offset, int length)
    {
        var result = UnsafeSlice(offset, length);
        result.Pin();
        return result;
    }

    /// <summary>
    /// 创建一块被此缓冲圈所连接的切片。
    /// </summary>
    /// <param name="offset">切片开始时的零基偏移。</param>
    /// <param name="length">切片的长度。</param>
    /// <returns>一个未喷漆切片, 此缓冲将引用页固定住, 其有效性保持不变 。</returns>
    public readonly ArcBuffer UnsafeSlice(int offset, int length)
    {
#if NET6_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Length - offset);
#else
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than or equal to 0.");
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be greater than or equal to 0.");
        if (offset > Length) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be less than or equal to the length of the buffer.");
        if (length > Length - offset) throw new ArgumentOutOfRangeException(nameof(length), "Length plus offset must be less than or equal to the length of the buffer.");
#endif

        CheckValidity();
        Debug.Assert(offset >= 0);
        Debug.Assert(length >= 0);
        Debug.Assert(length <= Length - offset);
        if (First is null) return Empty;
        var page = First;
        var pageOffset = Offset;
        while (offset > page.Length - pageOffset || (offset == page.Length - pageOffset && length > 0))
        {
            offset -= page.Length - pageOffset;
            page = page.Next;
            Debug.Assert(page is not null);
            pageOffset = 0;
        }

        return new ArcBuffer(page, pageOffset + offset, length);
    }

    /// <summary>
    /// 防止参考页被送回池子
    /// </summary>
    /// <remarks>
    /// 每通电话都另购一个自有别针。在交接前给每个自有副本
    /// 每种物的平衡,各取一个 <see cref="Unpin"/> 或 <see cref="Dispose"/>。
    /// </remarks>
    public readonly void Pin()
    {
        CheckValidity();
        if (First is null) return;
        if (IsEmpty)
        {
            First.Pin(_firstPageToken);
            return;
        }

        var pageEnumerator = Pages.GetEnumerator();
        if (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Pin(_firstPageToken);
        }

        while (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Pin(page.Version);
        }
    }

    /// <summary>
    /// 解开这个切片, 允许引用的页面返回到池中 。
    /// </summary>
    public void Unpin()
    {
        if (First is null) return;
        CheckValidity();
        if (IsEmpty)
        {
            First.Unpin(_firstPageToken);
            _firstPageToken = -1;
            return;
        }

        var pageEnumerator = Pages.GetEnumerator();
        if (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Unpin(_firstPageToken);
        }

        while (pageEnumerator.MoveNext())
        {
            var page = pageEnumerator.Current!;
            page.Unpin(page.Version);
        }

        _firstPageToken = -1;
    }

    /// <inheritdoc/>
    public void Dispose() => Unpin();

    /// <summary>
    /// 返回一个可以用来计算此例引用的宽段的点数。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    public readonly SpanEnumerator GetEnumerator() => new(this);

    /// <summary>
    /// 返回一个可以用来计算此例引用的页面的数值。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    internal readonly PageEnumerator Pages => new(this);

    /// <summary>
    /// 返回一个可以用来计算此例引用的页面的数值。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    internal readonly PageSegmentEnumerator PageSegments
    {
        get
        {
            CheckValidity();
            return new(this);
        }
    }

    /// <summary>
    /// 返回一个可以用来计算此例引用的宽段的点数。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    public readonly SpanEnumerator SpanSegments => new(this);

    /// <summary>
    /// 返回一个可以用来计算此实例引用的内存段的数值。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    public readonly MemoryEnumerator MemorySegments => new(this);

    /// <summary>
    /// 返回一个可以用来计算此实例引用的数组段的量子。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    public readonly ArraySegmentEnumerator ArraySegments => new(this);

    /// <summary>
    /// 定义页面中的数据区域。
    /// </summary>
    public readonly struct PageSegment(ArcBufferPage page, int offset, int length)
    {
        /// <summary>
        /// 获取此段所指页。
        /// </summary>
        public readonly ArcBufferPage Page = page;

        /// <summary>
        /// 将偏移移到此区域起始的页面中。
        /// </summary>
        public readonly int Offset = offset;

        /// <summary>
        /// 得到这个区域的时间长度。
        /// </summary>
        public readonly int Length = length;

        /// <summary>
        /// 获得该地区 <see cref="ReadOnlySpan{T}"/> 的代表。
        /// </summary>
        public readonly ReadOnlySpan<byte> Span => Page.AsSpan(Offset, Length);

        /// <summary>
        /// 获得该地区 <see cref="ReadOnlyMemory{T}"/> 的代表。
        /// </summary>
        public readonly ReadOnlyMemory<byte> Memory => Page.AsMemory(Offset, Length);

        /// <summary>
        /// 获得该地区 <see cref="ArraySegment{T}"/> 的代表。
        /// </summary>
        public readonly ArraySegment<byte> ArraySegment => Page.AsArraySegment(Offset, Length);
    }

    /// <summary>
    /// 在 <see cref="ArcBuffer"/>中页面段上的数字。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="PageSegmentEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">缓冲点要加。</param>
    internal struct PageSegmentEnumerator(ArcBuffer slice) : IEnumerable<PageSegment>, IEnumerator<PageSegment>
    {
        internal readonly ArcBuffer Slice = slice;
        private int _position;
        private ArcBufferPage? _page = slice.Length > 0 ? slice.First : null;

        internal readonly ArcBufferPage? First => Slice.First;
        internal readonly int Offset => Slice.Offset;
        internal readonly int Length => Slice.Length;

        /// <summary>
        /// 获取此实例作为点数器 。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly PageSegmentEnumerator GetEnumerator() => this;

        /// <summary>
        /// 将收藏中的元素移到当前点的分子位置。
        /// </summary>
        public PageSegment Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 获得一个数值,显示是否已完成查点。
        /// </summary>
        public readonly bool IsCompleted => _page is null || _position == Length;

        /// <summary>
        /// 将分子推进到收藏的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            if (_position == 0 && (Length == 0 || _page == First)) Slice.CheckValidity();
            Debug.Assert(_position <= Length);
            if (_page is null || _position == Length)
            {
                Current = default;
                Debug.Assert(_position == Length);
                return false;
            }

            if (_page == First)
            {
                Debug.Assert(_position == 0);
                var offset = Offset;
                var length = Math.Min(Length, _page.Length - offset);
                _position += length;
                Current = new PageSegment(_page, offset, length);
                _page = _page.Next;
                return true;
            }

            {
                var length = Math.Min(Length - _position, _page.Length);
                _position += length;
                Current = new PageSegment(_page, 0, length);
                _page = _page.Next;
                return true;
            }
        }

        /// <inheritdoc/>
        readonly IEnumerator<PageSegment> IEnumerable<PageSegment>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _position = 0;
            _page = Slice.Length > 0 ? Slice.First : null;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    /// <summary>
    /// 在 <see cref="ArcBuffer"/>中从页面上列出数字。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="PageEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">切片要加。</param>
    internal struct PageEnumerator(ArcBuffer slice) : IEnumerable<ArcBufferPage>, IEnumerator<ArcBufferPage?>
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 获取此实例作为点数器 。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly PageEnumerator GetEnumerator() => this;

        /// <summary>
        /// 将收藏中的元素移到当前点的分子位置。
        /// </summary>
        public ArcBufferPage? Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 将分子推进到收藏的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.Page;
                return true;
            }

            Current = default;
            return false;
        }

        /// <inheritdoc/>
        readonly IEnumerator<ArcBufferPage> IEnumerable<ArcBufferPage>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _enumerator = _enumerator.Slice.PageSegments;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    /// <summary>
    /// 在 <see cref="ArcBuffer"/>中横跨字节的数值。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="SpanEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">切片要加。</param>
    public ref struct SpanEnumerator(ArcBuffer slice)
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 获取此实例作为点数器 。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly SpanEnumerator GetEnumerator() => this;

        /// <summary>
        /// 将收藏中的元素移到当前点的分子位置。
        /// </summary>
        public ReadOnlySpan<byte> Current { get; private set; }

        /// <summary>
        /// 将分子推进到收藏的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.Span;
                return true;
            }

            Current = default;
            return false;
        }
    }

    /// <summary>
    /// 在 <see cref="ArcBuffer"/>中,对字节序列的数。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="MemoryEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">切片要加。</param>
    public struct MemoryEnumerator(ArcBuffer slice) : IEnumerable<ReadOnlyMemory<byte>>, IEnumerator<ReadOnlyMemory<byte>>
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 获取此实例作为点数器 。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly MemoryEnumerator GetEnumerator() => this;

        /// <summary>
        /// 将收藏中的元素移到当前点的分子位置。
        /// </summary>
        public ReadOnlyMemory<byte> Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 将分子推进到收藏的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.Memory;
                return true;
            }

            Current = default;
            return false;
        }

        /// <inheritdoc/>
        readonly IEnumerator<ReadOnlyMemory<byte>> IEnumerable<ReadOnlyMemory<byte>>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _enumerator = _enumerator.Slice.PageSegments;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    /// <summary>
    /// 在 <see cref="ArcBuffer"/>中,在数组区段上进行点数。
    /// </summary>
    /// <remarks>
    /// 初始化 <see cref="ArraySegmentEnumerator"/> 类型的新实例。
    /// </remarks>
    /// <param name="slice">切片要加。</param>
    public struct ArraySegmentEnumerator(ArcBuffer slice) : IEnumerable<ArraySegment<byte>>, IEnumerator<ArraySegment<byte>>
    {
        private PageSegmentEnumerator _enumerator = slice.PageSegments;

        /// <summary>
        /// 获取此实例作为点数器 。
        /// </summary>
        /// <returns>此实例。</returns>
        public readonly ArraySegmentEnumerator GetEnumerator() => this;

        /// <summary>
        /// 将收藏中的元素移到当前点的分子位置。
        /// </summary>
        public ArraySegment<byte> Current { get; private set; }

        /// <inheritdoc/>
        readonly object? IEnumerator.Current => Current;

        /// <summary>
        /// 获得一个数值,显示是否已完成查点。
        /// </summary>
        public readonly bool IsCompleted => _enumerator.IsCompleted;

        /// <summary>
        /// 将分子推进到收藏的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            if (_enumerator.MoveNext())
            {
                Current = _enumerator.Current.ArraySegment;
                return true;
            }

            Current = default;
            return false;
        }

        /// <inheritdoc/>
        readonly IEnumerator<ArraySegment<byte>> IEnumerable<ArraySegment<byte>>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void IEnumerator.Reset()
        {
            _enumerator = _enumerator.Slice.PageSegments;
            Current = default;
        }

        /// <inheritdoc/>
        readonly void IDisposable.Dispose() { }
    }

    private sealed class ReadOnlySequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public ReadOnlySequenceSegment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public void SetNext(ReadOnlySequenceSegment next)
        {
            Debug.Assert(Next is null);
            Next = next;
        }
    }
}
