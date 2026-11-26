using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET6_0_OR_GREATER
using System.Numerics;
#else
using Orleans.Serialization.Utilities;
#endif

namespace Orleans.Serialization.Buffers;

/// <summary>
/// <see cref="IBufferWriter{T}"/> 实施使用专门用来创建 <see cref="ReadOnlySequence{T}"/> 实例的集合阵列。
/// </summary>
[StructLayout(LayoutKind.Auto)]
[Immutable]
public partial struct PooledBuffer : IBufferWriter<byte>, IDisposable
{
    internal SequenceSegment? First;
    internal SequenceSegment? Last;
    internal SequenceSegment? WriteHead;
    internal int TotalLength;
    internal int CurrentPosition;

    /// <summary>
    /// 初始化 <see cref="PooledBuffer"/> 结构的新实例。
    /// </summary>
    public PooledBuffer()
    {
        First = Last = null;
        WriteHead = null;
        TotalLength = 0;
        CurrentPosition = 0;
    }

    /// <summary>获取已写入的长度。</summary>
    public readonly int Length => TotalLength + CurrentPosition;

    /// <summary>
    /// 返回以数组形式写入的数据。
    /// </summary>
    /// <returns>已经写入的数据 。</returns>
    public readonly byte[] ToArray()
    {
        var result = new byte[Length];
        var resultSpan = result.AsSpan();
        var current = First;
        while (current != null)
        {
            var span = current.CommittedMemory.Span;
            span.CopyTo(resultSpan);
            resultSpan = resultSpan.Slice(span.Length);
            current = current.Next as SequenceSegment;
        }

        if (CurrentPosition > 0)
        {
            WriteHead!.Array.AsSpan(0, CurrentPosition).CopyTo(resultSpan);
        }

        return result;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytes)
    {
        if (WriteHead is null || CurrentPosition + bytes > WriteHead.Array.Length)
        {
            ThrowInvalidOperation();
        }

        CurrentPosition += bytes;

        static void ThrowInvalidOperation() => throw new InvalidOperationException("Attempted to advance past the end of a buffer.");
    }

    /// <summary>
    /// 重设此实例, 返回全部内存 。
    /// </summary>
    public void Reset()
    {
        var current = First;
        while (current != null)
        {
            var previous = current;
            current = previous.Next as SequenceSegment;
            previous.Return();
            Debug.Assert(current == null || current != WriteHead);
        }

        WriteHead?.Return();

        First = Last = WriteHead = null;
        CurrentPosition = TotalLength = 0;
    }

    /// <inheritdoc/>
    public void Dispose() => Reset();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (WriteHead is null || sizeHint >= WriteHead.Array.Length - CurrentPosition)
        {
            return GetMemorySlow(sizeHint);
        }

        return WriteHead.AsMemory(CurrentPosition);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (WriteHead is { Array: var head } && sizeHint < head.Length - CurrentPosition)
        {
            return head.AsSpan(CurrentPosition);
        }

        return GetSpanSlow(sizeHint);
    }

    /// <summary>将此作家的内容复制到一个字符串。</summary>
    public readonly void CopyTo(Span<byte> output)
    {
        var current = First;
        while (output.Length > 0 && current != null)
        {
            var segment = current.CommittedMemory.Span;
            var slice = segment[..Math.Min(segment.Length, output.Length)];
            slice.CopyTo(output);
            output = output[slice.Length..];
            current = current.Next as SequenceSegment;
        }

        if (output.Length > 0 && CurrentPosition > 0)
        {
            var span = WriteHead!.Array.AsSpan(0, Math.Min(output.Length, CurrentPosition));
            span.CopyTo(output);
        }
    }

    /// <summary>将此作家的内容复制到另一个作家。</summary>
    public readonly void CopyTo<TBufferWriter>(ref Writer<TBufferWriter> writer) where TBufferWriter : IBufferWriter<byte>
    {
        var current = First;
        while (current != null)
        {
            var span = current.CommittedMemory.Span;
            writer.Write(span);
            current = current.Next as SequenceSegment;
        }

        if (CurrentPosition > 0)
        {
            writer.Write(WriteHead!.Array.AsSpan(0, CurrentPosition));
        }
    }

    /// <summary>将此作家的内容复制到另一个作家。</summary>
    public readonly void CopyTo<TBufferWriter>(ref TBufferWriter writer) where TBufferWriter : IBufferWriter<byte>
    {
        var current = First;
        while (current != null)
        {
            var span = current.CommittedMemory.Span;
            Adaptors.BufferWriterExtensions.Write(ref writer, span);
            current = current.Next as SequenceSegment;
        }

        if (CurrentPosition > 0)
        {
            Adaptors.BufferWriterExtensions.Write(ref writer, WriteHead!.Array.AsSpan(0, CurrentPosition));
        }
    }

    /// <summary>
    /// 返回新的 <see cref="ReadOnlySequence{T}"/> ,在处理此实例后不得访问。
    /// </summary>
    public ReadOnlySequence<byte> AsReadOnlySequence()
    {
        if (Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        Commit();
        if (First == Last)
        {
            return new ReadOnlySequence<byte>(First!.CommittedMemory);
        }

        return new ReadOnlySequence<byte>(First!, 0, Last!, Last!.CommittedMemory.Length);
    }

    /// <summary>
    /// a 返回 <see cref="BufferSlice"/> ,涵盖整个缓冲。
    /// </summary>
    /// <remarks>
    /// 返回的 <see cref="BufferSlice"/> 寿命必须短于本例的寿命。
    /// </remarks>
    /// <returns>A <see cref="BufferSlice"/> 覆盖整个缓冲。</returns>
    public readonly BufferSlice Slice() => new(this, 0, Length);

    /// <summary>
    /// 返回此缓冲的片段, 从指定偏移开始 。
    /// </summary>
    /// <remarks>
    /// 返回的 <see cref="BufferSlice"/> 寿命必须短于本例的寿命。
    /// </remarks>
    /// <returns>一个切片,代表从指定偏移开始的本例子集。</returns>
    public readonly BufferSlice Slice(int offset) => new(this, offset, Length - offset);

    /// <summary>
    /// 返回此缓冲片的切片, 从指定偏移开始, 并具有指定的长度 。
    /// </summary>
    /// <remarks>
    /// 返回的 <see cref="BufferSlice"/> 寿命必须短于本例的寿命。
    /// </remarks>
    /// <returns>一个切片,代表从指定偏移开始的本例子集。</returns>
    public readonly BufferSlice Slice(int offset, int length) => new(this, offset, length);

    /// <summary>
    /// 返回一个可以用来计算此实例引用数据的数值。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    public readonly MemoryEnumerator MemorySegments => new(this);

    /// <summary>
    /// 将提供的序列写入此缓冲。
    /// </summary>
    /// <param name="input">要写入的数据。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySequence<byte> input)
    {
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
        // 快路径, 尝试直接复制到可用内存
        if (WriteHead is { Array: { } head })
        {
            var destination = head.AsSpan(CurrentPosition);
            if ((uint)value.Length <= (uint)destination.Length)
            {
                value.CopyTo(destination);
                CurrentPosition += value.Length;
                return;
            }
        }

        WriteMultiSegment(value);
    }

    private void WriteMultiSegment(in ReadOnlySpan<byte> source)
    {
        var input = source;
        while (true)
        {
            var destination = GetSpan();
            var writeSize = Math.Min(destination.Length, input.Length);
            input[..writeSize].CopyTo(destination);
            CurrentPosition += writeSize;
            input = input.Slice(writeSize);
            if (input.Length > 0)
            {
                continue;
            }

            return;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Span<byte> GetSpanSlow(int sizeHint) => Grow(sizeHint).Array;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Memory<byte> GetMemorySlow(int sizeHint) => Grow(sizeHint).AsMemory(0);

    private SequenceSegment Grow(int sizeHint)
    {
        Commit();
        var newBuffer = SequenceSegmentPool.Shared.Rent(sizeHint);
        return WriteHead = newBuffer;
    }

    private void Commit()
    {
        if (CurrentPosition == 0 || WriteHead is null)
        {
            return;
        }

        var writeHead = WriteHead;
        writeHead.Commit(TotalLength, CurrentPosition);
        TotalLength += CurrentPosition;
        if (First is null)
        {
            First = writeHead;
        }
        else
        {
            Debug.Assert(Last is not null);
            Last!.SetNext(writeHead);
        }

        Last = writeHead;
        WriteHead = null;
        CurrentPosition = 0;
    }

    /// <summary>
    /// 在 <see cref="PooledBuffer"/>中,对字节序列的数。
    /// </summary>
    public struct MemoryEnumerator
    {
        private static readonly SequenceSegment InitialSegmentSentinel = new();
        private static readonly SequenceSegment FinalSegmentSentinel = new();
        private readonly PooledBuffer _buffer;
        private int _position;
        private SequenceSegment? _segment;

        /// <summary>
        /// 初始化 <see cref="MemoryEnumerator"/> 类型的新实例。
        /// </summary>
        /// <param name="buffer">缓冲点要加。</param>
        public MemoryEnumerator(PooledBuffer buffer)
        {
            _buffer = buffer;
            _segment = InitialSegmentSentinel;
            CurrentMemory = ReadOnlyMemory<byte>.Empty;
        }

        /// <summary>
        /// 返回一个可以用来计算此实例引用数据的数值。
        /// </summary>
        /// <returns>用于此例中所含数据的计算器。</returns>
        public readonly MemoryEnumerator GetEnumerator() => this;

        /// <summary>
        /// 将收藏中的元素移到当前点的分子位置。
        /// </summary>
        public readonly ReadOnlyMemory<byte> Current => CurrentMemory;

        /// <summary>
        /// 分子当前位置的内存。
        /// </summary>
        public ReadOnlyMemory<byte> CurrentMemory;

        /// <summary>
        /// 将分子推进到收藏的下一个元素。
        /// </summary>
        /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
        public bool MoveNext()
        {
            if (ReferenceEquals(_segment, InitialSegmentSentinel))
            {
                _segment = _buffer.First;
            }

            var endPosition = _buffer.Length;
            while (_segment != null && _segment != FinalSegmentSentinel)
            {
                var segment = _segment.CommittedMemory;

                // 查找起始段和要复制的偏移。
                var segmentLength = segment.Length;
                CurrentMemory = segment[..segmentLength];
                _position += segmentLength;
                _segment = _segment.Next as SequenceSegment;
                return true;
            }

            // 缓冲符末尾未交付数据账户。
            // 写入头仅与前一个缓冲连接, 当调用 $[] 时, 它会设置为无效, 然后将无效,
            // 意思是,如果写头不是空的,其他缓冲没有与之连接,因此也没有列出。
            if (_segment != FinalSegmentSentinel && _buffer.CurrentPosition > 0 && _buffer.WriteHead is { } head)
            {
                var finalLength = _buffer.CurrentPosition;
                CurrentMemory = head.Array.AsMemory(0, finalLength);
                _position += finalLength;
                Debug.Assert(_position == _buffer.Length);
                _segment = FinalSegmentSentinel;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 在 <see cref="PooledBuffer"/>中,对字节序列的数。
    /// </summary>
    public ref struct SpanEnumerator
    {
        private static readonly SequenceSegment InitialSegmentSentinel = new();
        private static readonly SequenceSegment FinalSegmentSentinel = new();
        private readonly
#if NET8_0_OR_GREATER
            ref readonly
#endif
            PooledBuffer _buffer;
        private int _position;
        private SequenceSegment? _segment;

        /// <summary>
        /// 初始化 <see cref="SpanEnumerator"/> 类型的新实例。
        /// </summary>
        /// <param name="buffer">缓冲点要加。</param>
        public SpanEnumerator(ref PooledBuffer buffer)
        {
            _buffer =
#if NET8_0_OR_GREATER
                ref
#endif
                buffer;
            _segment = InitialSegmentSentinel;
            Current = ReadOnlySpan<byte>.Empty;
        }

        /// <summary>
        /// 返回一个可以用来计算此实例引用数据的数值。
        /// </summary>
        /// <returns>用于此例中所含数据的计算器。</returns>
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
            if (ReferenceEquals(_segment, InitialSegmentSentinel))
            {
                _segment = _buffer.First;
            }

            var endPosition = _buffer.Length;
            while (_segment != null && _segment != FinalSegmentSentinel)
            {
                var segment = _segment.CommittedMemory;

                // 查找起始段和要复制的偏移。
                var segmentLength = Math.Min(segment.Length, endPosition - _position);
                Current = segment.Span[..segmentLength];
                _position += segmentLength;
                _segment = _segment.Next as SequenceSegment;
                return true;
            }

            // 缓冲符末尾未交付数据账户。
            // 写入头仅与前一个缓冲连接, 当调用 $[] 时, 它会设置为无效, 然后将无效,
            // 意思是,如果写头不是空的,其他缓冲没有与之连接,因此也没有列出。
            if (_segment != FinalSegmentSentinel && _buffer.CurrentPosition > 0 && _buffer.WriteHead is { } head && _position < endPosition)
            {
                var finalLength = Math.Min(_buffer.CurrentPosition, endPosition - _position);
                Current = head.Array.AsSpan(0, finalLength);
                _position += finalLength;
                Debug.Assert(_position == endPosition);
                _segment = FinalSegmentSentinel;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 表示一个 <see cref="PooledBuffer"/>的片段。
    /// </summary>
    public readonly struct BufferSlice
    {
#pragma warning disable IDE1006 // Naming Styles
        internal readonly PooledBuffer _buffer;
        internal readonly int _offset;
        internal readonly int _length;
#pragma warning restore IDE1006 // Naming Styles

        /// <summary>
        /// 初始化 <see cref="BufferSlice"/> 类型的新实例。
        /// </summary>
        /// <param name="buffer">缓冲。</param>
        /// <param name="offset">切片开始的缓冲中偏移。</param>
        /// <param name="length">此切片的长度。</param>
        public BufferSlice(in PooledBuffer buffer, int offset, int length)
        {
            _buffer = buffer;
            _offset = offset;
            _length = length;
        }

        /// <summary>
        /// 获取底部 <see cref="PooledBuffer"/>。
        /// </summary>
        public readonly PooledBuffer Buffer => _buffer;

        /// <summary>
        /// 将偏移点移入开始切片的缓冲底部。
        /// </summary>
        public readonly int Offset => _offset;

        /// <summary>
        /// 得到这个切片的长度。
        /// </summary>
        public readonly int Length => _length;

        /// <summary>
        /// 窗体从此实例中选择一个切片, 从指定的偏移开始到这个切片 。
        /// </summary>
        /// <param name="offset">切片的偏移将进入此切片, 开始新形成的切片。</param>
        /// <returns>一个切片实例。</returns>
        public readonly BufferSlice Slice(int offset) => new(in _buffer, _offset + offset, _length - offset);

        /// <summary>
        /// 窗体中从此实例的切片, 从指定的偏移开始, 以指定长度为单位。
        /// </summary>
        /// <param name="offset">切片的偏移将进入此切片, 开始新形成的切片。</param>
        /// <param name="length">新切片的长度。</param>
        /// <returns>一个切片实例。</returns>
        public readonly BufferSlice Slice(int offset, int length) => new(in _buffer, _offset + offset, length);

        /// <summary>将此作家的内容复制到一个字符串。</summary>
        public readonly int CopyTo(Span<byte> output)
        {
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
        public readonly void CopyTo(ref PooledBuffer output)
        {
            foreach (var span in this)
            {
                output.Write(span);
            }
        }

        /// <summary>将此作家的内容复制到缓冲作者。</summary>
        public readonly void CopyTo<TBufferWriter>(ref TBufferWriter output) where TBufferWriter : struct, IBufferWriter<byte>
        {
            foreach (var span in this)
            {
                Adaptors.BufferWriterExtensions.Write(ref output, span);
            }
        }

        /// <summary>
        /// 返回以数组形式写入的数据。
        /// </summary>
        /// <returns>已经写入的数据 。</returns>
        public readonly byte[] ToArray()
        {
            var result = new byte[_length];
            CopyTo(result);
            return result;
        }

        /// <summary>
        /// 返回一个可以用来计算此实例引用数据的数值。
        /// </summary>
        /// <returns>用于此例中所含数据的计算器。</returns>
        public readonly SpanEnumerator GetEnumerator() => new(this);

        /// <summary>
        /// 返回一个可以用来计算此实例引用数据的数值。
        /// </summary>
        /// <returns>用于此例中所含数据的计算器。</returns>
        public readonly MemoryEnumerator MemorySegments => new(this);

        /// <summary>
        /// 在 <see cref="BufferSlice"/>中横跨字节的数值。
        /// </summary>
        public ref struct SpanEnumerator
        {
            private static readonly SequenceSegment InitialSegmentSentinel = new();
            private static readonly SequenceSegment FinalSegmentSentinel = new();
            private readonly BufferSlice _slice;
            private int _position;
            private SequenceSegment? _segment;

            /// <summary>
            /// 初始化 <see cref="SpanEnumerator"/> 类型的新实例。
            /// </summary>
            /// <param name="slice">切片要加。</param>
            public SpanEnumerator(BufferSlice slice)
            {
                _slice = slice;
                _segment = InitialSegmentSentinel;
                Current = Span<byte>.Empty;
            }

            internal readonly PooledBuffer Buffer => _slice._buffer;
            internal readonly int Offset => _slice._offset;
            internal readonly int Length => _slice._length;

            /// <summary>
            /// 返回一个可以用来计算此实例引用数据的数值。
            /// </summary>
            /// <returns>用于此例中所含数据的计算器。</returns>
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
                if (ReferenceEquals(_segment, InitialSegmentSentinel))
                {
                    _segment = Buffer.First;
                }

                var endPosition = Offset + Length;
                while (_segment != null && _segment != FinalSegmentSentinel)
                {
                    var segment = _segment.CommittedMemory.Span;

                    // 查找起始段和要复制的偏移。
                    int segmentOffset;
                    if (_position < Offset)
                    {
                        if (_position + segment.Length <= Offset)
                        {
                            // 启动于随后的一段
                            _position += segment.Length;
                            _segment = _segment.Next as SequenceSegment;
                            continue;
                        }
                        else
                        {
                            // 启动于此段
                            segmentOffset = Offset - _position;
                        }
                    }
                    else
                    {
                        segmentOffset = 0;
                    }

                    var segmentLength = Math.Min(segment.Length - segmentOffset, endPosition - (_position + segmentOffset));
                    if (segmentLength == 0)
                    {
                        Current = Span<byte>.Empty;
                        _segment = FinalSegmentSentinel;
                        return false;
                    }

                    Current = segment.Slice(segmentOffset, segmentLength);
                    _position += segmentOffset + segmentLength;
                    _segment = _segment.Next as SequenceSegment;
                    return true;
                }

                // 缓冲符末尾未交付数据账户。
                // 写入头仅与前一个缓冲连接, 当调用 $[] 时, 它会设置为无效, 然后将无效,
                // 意思是,如果写头不是空的,其他缓冲没有与之连接,因此也没有列出。
                if (_segment != FinalSegmentSentinel && Buffer.CurrentPosition > 0 && Buffer.WriteHead is { } head && _position < endPosition)
                {
                    var finalOffset = Math.Max(Offset - _position, 0);
                    var finalLength = Math.Min(Buffer.CurrentPosition, endPosition - (_position + finalOffset));
                    if (finalLength == 0)
                    {
                        Current = Span<byte>.Empty;
                        _segment = FinalSegmentSentinel;
                        return false;
                    }

                    Current = head.Array.AsSpan(finalOffset, finalLength);
                    _position += finalOffset + finalLength;
                    Debug.Assert(_position == endPosition);
                    _segment = FinalSegmentSentinel;
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// 在 <see cref="BufferSlice"/>中,对字节序列的数。
        /// </summary>
        public struct MemoryEnumerator
        {
            private static readonly SequenceSegment InitialSegmentSentinel = new();
            private static readonly SequenceSegment FinalSegmentSentinel = new();
            private readonly BufferSlice _slice;
            private int _position;
            private SequenceSegment? _segment;

            /// <summary>
            /// 初始化 <see cref="MemoryEnumerator"/> 类型的新实例。
            /// </summary>
            /// <param name="slice">切片要加。</param>
            public MemoryEnumerator(BufferSlice slice)
            {
                _slice = slice;
                _segment = InitialSegmentSentinel;
                Current = ReadOnlyMemory<byte>.Empty;
            }

            internal readonly PooledBuffer Buffer => _slice._buffer;
            internal readonly int Offset => _slice._offset;
            internal readonly int Length => _slice._length;

            /// <summary>
            /// 返回一个可以用来计算此实例引用数据的数值。
            /// </summary>
            /// <returns>用于此例中所含数据的计算器。</returns>
            public readonly MemoryEnumerator GetEnumerator() => this;

            /// <summary>
            /// 将收藏中的元素移到当前点的分子位置。
            /// </summary>
            public ReadOnlyMemory<byte> Current { get; private set; }

            /// <summary>
            /// 将分子推进到收藏的下一个元素。
            /// </summary>
            /// <returns><see langword="true"/> 如果该分子成功地被推进到下一个元素; <see langword="false"/> 如果该分子已经超过收藏的终点。</returns>
            public bool MoveNext()
            {
                if (ReferenceEquals(_segment, InitialSegmentSentinel))
                {
                    _segment = Buffer.First;
                }

                var endPosition = Offset + Length;
                while (_segment != null && _segment != FinalSegmentSentinel)
                {
                    var segment = _segment.CommittedMemory;

                    // 查找起始段和要复制的偏移。
                    int segmentOffset;
                    if (_position < Offset)
                    {
                        if (_position + segment.Length <= Offset)
                        {
                            // 启动于随后的一段
                            _position += segment.Length;
                            _segment = _segment.Next as SequenceSegment;
                            continue;
                        }
                        else
                        {
                            // 启动于此段
                            segmentOffset = Offset - _position;
                        }
                    }
                    else
                    {
                        segmentOffset = 0;
                    }

                    var segmentLength = Math.Min(segment.Length - segmentOffset, endPosition - (_position + segmentOffset));
                    if (segmentLength == 0)
                    {
                        Current = ReadOnlyMemory<byte>.Empty;
                        _segment = FinalSegmentSentinel;
                        return false;
                    }

                    Current = segment.Slice(segmentOffset, segmentLength);
                    _position += segmentOffset + segmentLength;
                    _segment = _segment.Next as SequenceSegment;
                    return true;
                }

                // 缓冲符末尾未交付数据账户。
                // 写入头仅与前一个缓冲连接, 当调用 $[] 时, 它会设置为无效, 然后将无效,
                // 意思是,如果写头不是空的,其他缓冲没有与之连接,因此也没有列出。
                if (_segment != FinalSegmentSentinel && Buffer.CurrentPosition > 0 && Buffer.WriteHead is { } head && _position < endPosition)
                {
                    var finalOffset = Math.Max(Offset - _position, 0);
                    var finalLength = Math.Min(Buffer.CurrentPosition, endPosition - (_position + finalOffset));
                    if (finalLength == 0)
                    {
                        Current = ReadOnlyMemory<byte>.Empty;
                        _segment = FinalSegmentSentinel;
                        return false;
                    }

                    Current = head.Array.AsMemory(finalOffset, finalLength);
                    _position += finalOffset + finalLength;
                    Debug.Assert(_position == endPosition);
                    _segment = FinalSegmentSentinel;
                    return true;
                }

                return false;
            }
        }
    }

    private sealed class SequenceSegmentPool
    {
        public static SequenceSegmentPool Shared { get; } = new();
        public const int MinimumBlockSize = 4 * 1024;
        private readonly ConcurrentQueue<SequenceSegment> _blocks = new();
        private readonly ConcurrentQueue<SequenceSegment> _largeBlocks = new();

        private SequenceSegmentPool() { }

        public SequenceSegment Rent(int size = -1)
        {
            SequenceSegment? block;
            if (size <= MinimumBlockSize)
            {
                return _blocks.TryDequeue(out block) ? block! : new(large: false);
            }

            if (!_largeBlocks.TryDequeue(out block))
            {
                block = new(large: true);
            }
            block!.ResizeLargeSegment(size);
            return block;
        }

        internal void Return(SequenceSegment block)
        {
            Debug.Assert(block.IsValid);
            (block.IsMinimumSize ? _blocks : _largeBlocks).Enqueue(block);
        }
    }

    internal sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
    {
        internal SequenceSegment()
        {
            Array = System.Array.Empty<byte>();
        }

        internal SequenceSegment(bool large)
        {
            if (!large)
            {
#if NET6_0_OR_GREATER
                Array = GC.AllocateUninitializedArray<byte>(SequenceSegmentPool.MinimumBlockSize, pinned: true);
#else
                Array = new byte[SequenceSegmentPool.MinimumBlockSize];
#endif
            }
        }

        public void ResizeLargeSegment(int length)
        {
            Debug.Assert(length > SequenceSegmentPool.MinimumBlockSize);
            // 集中到两个力量
            length = (int)BitOperations.RoundUpToPowerOf2((uint)length);

            if (Array is { } array)
            {
                // 段的大小已经适当。
                if (array.Length == length)
                {
                    return;
                }

                // 正在调整该段的大小。
                Array = null!;
                ArrayPool<byte>.Shared.Return(array);
            }

            Array = ArrayPool<byte>.Shared.Rent(length);
        }

        public byte[] Array { get; private set; } = System.Array.Empty<byte>();

        public ReadOnlyMemory<byte> CommittedMemory => Memory;

        public bool IsValid => Array is { Length: > 0 };
        public bool IsMinimumSize => Array.Length == SequenceSegmentPool.MinimumBlockSize;

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

        public void Commit(long runningIndex, int length)
        {
            RunningIndex = runningIndex;
            Memory = AsMemory(0, length);
        }

        public void SetNext(SequenceSegment next) => Next = next;

        public void Return()
        {
            RunningIndex = default;
            Next = default;
            Memory = default;

            SequenceSegmentPool.Shared.Return(this);
        }
    }
}

/// <summary>
/// <see cref="PooledBuffer"/>的扩展期。
/// </summary>
public static class PooledBufferExtensions
{
    /// <summary>
    /// 返回一个可以用来计算此实例引用数据的数值。
    /// </summary>
    /// <returns>用于此例中所含数据的计算器。</returns>
    public static PooledBuffer.SpanEnumerator GetEnumerator(this ref PooledBuffer buffer) => new(ref buffer);
}
