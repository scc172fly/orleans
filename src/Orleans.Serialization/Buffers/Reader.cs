using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
#if NETCOREAPP3_1_OR_GREATER
using System.Numerics;
#endif
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Session;
using static Orleans.Serialization.Buffers.PooledBuffer;
#if !NETCOREAPP3_1_OR_GREATER
using Orleans.Serialization.Utilities;
#endif

namespace Orleans.Serialization.Buffers
{
    /// <summary>
    /// 阅读二元数据功能。
    /// </summary>
    public abstract class ReaderInput
    {
        /// <summary>
        /// 获得位置。
        /// </summary>
        /// <value>位置。</value>
        public abstract long Position { get; }

        /// <summary>
        /// 长了
        /// </summary>
        /// <value>长度。</value>
        public abstract long Length { get; }

        /// <summary>
        /// 跳过指定的字节数 。
        /// </summary>
        /// <param name="count">要跳过的字节数。</param>
        public abstract void Skip(long count);

        /// <summary>
        /// 寻找指定位置。
        /// </summary>
        /// <param name="position">位置。</param>
        public abstract void Seek(long position);

        /// <summary>
        /// 读取输入中的字节。
        /// </summary>
        /// <returns>读到的字节</returns>
        public abstract byte ReadByte();

        /// <summary>
        /// 从输入读取 <see cref="uint"/> 。
        /// </summary>
        /// <returns>下 <see cref="uint"/> 后经宣读。</returns>
        public abstract uint ReadUInt32();

        /// <summary>
        /// 从输入读取 <see cref="ulong"/> 。
        /// </summary>
        /// <returns>下 <see cref="ulong"/> 后经宣读。</returns>
        public abstract ulong ReadUInt64();

        /// <summary>
        /// 填充输入中的数据,以填充目的地的宽度。
        /// </summary>
        /// <param name="destination">目的地</param>
        public abstract void ReadBytes(Span<byte> destination);

        /// <summary>
        /// 从输入到目标数组的读取字节。
        /// </summary>
        /// <param name="destination">目标数组。</param>
        /// <param name="offset">将偏移到开始写入字节的目的地。</param>
        /// <param name="length">要复制到目的地的字节数。</param>
        public abstract void ReadBytes(byte[] destination, int offset, int length);

        /// <summary>
        /// 尝试读取输入中指定的字节数 。
        /// </summary>
        /// <param name="length">要读取的字节数。</param>
        /// <param name="bytes">读取的字节。</param>
        /// <returns><see langword="true"/> 如果成功读取字节数,则 <see langword="false"/> 。</returns>
        public abstract bool TryReadBytes(int length, out ReadOnlySpan<byte> bytes);
    }

    internal sealed class StreamReaderInput : ReaderInput
    {
        [ThreadStatic]
        private static byte[]? Scratch;

        private readonly Stream _stream;
        private readonly ArrayPool<byte> _memoryPool;

        public override long Position => _stream.Position;
        public override long Length => _stream.Length;
        internal long Remaining => _stream.CanSeek ? _stream.Length - _stream.Position : long.MaxValue;

        public StreamReaderInput(Stream stream, ArrayPool<byte> memoryPool)
        {
            _stream = stream;
            _memoryPool = memoryPool;
        }

        public override byte ReadByte()
        {
            var c = _stream.ReadByte();
            if (c < 0)
            {
                ThrowInsufficientData();
            }

            return (byte)c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void ReadBytes(Span<byte> destination)
        {
            var count = _stream.Read(destination);
            if (count < destination.Length)
            {
                ThrowInsufficientData();
            }
        }

        public override void ReadBytes(byte[] destination, int offset, int length)
        {
            var count = _stream.Read(destination, offset, length);
            if (count < length)
            {
                ThrowInsufficientData();
            }
        }

#if NET5_0_OR_GREATER
        [SkipLocalsInit]
#endif
        public override uint ReadUInt32()
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            ReadBytes(buffer);
            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

#if NET5_0_OR_GREATER
        [SkipLocalsInit]
#endif
        public override ulong ReadUInt64()
        {
            Span<byte> buffer = stackalloc byte[sizeof(ulong)];
            ReadBytes(buffer);
            return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        public override void Skip(long count) => _ = _stream.Seek(count, SeekOrigin.Current);

        public override void Seek(long position) => _ = _stream.Seek(position, SeekOrigin.Begin);

        public override bool TryReadBytes(int length, out ReadOnlySpan<byte> destination)
        {
            // 无法在流的内缓冲处定位一个空格 。
            destination = default;
            return false;
        }

        private static void ThrowInsufficientData() => throw new InvalidOperationException("Insufficient data present in buffer.");

        private static byte[] GetScratchBuffer() => Scratch ??= new byte[1024];
    }

    /// <summary>
    /// <see cref="Reader{TInput}"/>的辅助方法。
    /// </summary>
    public static class Reader
    {
        /// <summary>
        /// 从第一个字节中获取变量-宽整数的位元计。
        /// </summary>
        /// <param name="firstByte">编码变量-宽整数的第一个字节。</param>
        /// <returns>编号的字节数 <paramref name="firstByte"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarIntByteCount(byte firstByte) => BitOperations.TrailingZeroCount(0x0100U | firstByte) + 1;

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<BufferSliceReaderInput> Create(PooledBuffer input, SerializerSession session) => Create(input.Slice(), session);

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<BufferSliceReaderInput> Create(BufferSlice input, SerializerSession session) => new(new BufferSliceReaderInput(in input), session, 0);

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        /// <remarks>输入必须保持有效,其引用字节在读者一生中保持不变。</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<ArcBufferReaderInput> Create(ArcBuffer input, SerializerSession session) => new(new ArcBufferReaderInput(in input), session, 0);

        /// <summary>
        /// 为所提供的输入流创建阅读器 。
        /// </summary>
        /// <param name="stream">流。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<ReaderInput> Create(Stream stream, SerializerSession session) => new(new StreamReaderInput(stream, ArrayPool<byte>.Shared), session, 0);

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="sequence">缓冲。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<ReadOnlySequenceInput> Create(ReadOnlySequence<byte> sequence, SerializerSession session) => new(new ReadOnlySequenceInput { Sequence = sequence }, session, 0);

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="buffer">缓冲。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<SpanReaderInput> Create(ReadOnlySpan<byte> buffer, SerializerSession session) => new(buffer, session, 0);

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="buffer">缓冲。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<SpanReaderInput> Create(byte[] buffer, SerializerSession session) => new(buffer, session, 0);

        /// <summary>
        /// 为提供的缓冲创建阅读器。
        /// </summary>
        /// <param name="buffer">缓冲。</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Reader{TInput}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Reader<SpanReaderInput> Create(ReadOnlyMemory<byte> buffer, SerializerSession session) => new(buffer.Span, session, 0);
    }

    /// <summary>
    /// 标记类型 <see cref="Reader{TInput}"/> 物体在 <see cref="ReadOnlySpan{Byte}"/> 缓冲带。
    /// </summary>
    public readonly struct SpanReaderInput
    {
    }

    /// <summary>
    /// <see cref="Reader{TInput}"/> 用于支持 <see cref="ReadOnlySequence{Byte}"/> 缓冲的输入类型。
    /// </summary>
    public struct ReadOnlySequenceInput
    {
        internal ReadOnlySequence<byte> Sequence;
        internal SequencePosition NextSequencePosition;
        internal long PreviousBuffersSize;
    }

    /// <summary>
    /// 提供从二进制输入解析数据的功能。
    /// </summary>
    /// <typeparam name="TInput">基本缓冲阅读器类型。</typeparam>
    public ref struct Reader<TInput>
    {
        private readonly static bool IsSpanInput = typeof(TInput) == typeof(SpanReaderInput);
        private readonly static bool IsReadOnlySequenceInput = typeof(TInput) == typeof(ReadOnlySequenceInput);
        private readonly static bool IsReaderInput = typeof(ReaderInput).IsAssignableFrom(typeof(TInput));
        private readonly static bool IsBufferSliceInput = typeof(TInput) == typeof(BufferSliceReaderInput);
        private readonly static bool IsArcBufferInput = typeof(TInput) == typeof(ArcBufferReaderInput);

        private ReadOnlySpan<byte> _currentSpan;
        private int _bufferPos;
        private int _bufferSize;
        private readonly long _sequenceOffset;
        private TInput _input;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Reader(TInput input, SerializerSession session, long globalOffset)
        {
            if (IsReadOnlySequenceInput)
            {
                ref var typedInput = ref Unsafe.As<TInput, ReadOnlySequenceInput>(ref input);
                var sequence = typedInput.Sequence;
                typedInput.NextSequencePosition = sequence.Start;
                _input = input;
                _currentSpan = sequence.First.Span;
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
                _sequenceOffset = globalOffset;
            }
            else if (IsBufferSliceInput)
            {
                _input = input;
                ref var slice = ref Unsafe.As<TInput, BufferSliceReaderInput>(ref _input);
                _currentSpan = slice.GetNext();
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
                _sequenceOffset = globalOffset;
            }
            else if (IsArcBufferInput)
            {
                _input = input;
                ref var slice = ref Unsafe.As<TInput, ArcBufferReaderInput>(ref _input);
                _currentSpan = slice.GetNext();
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
                _sequenceOffset = globalOffset;
            }
            else if (IsReaderInput)
            {
                _input = input;
                _currentSpan = default;
                _bufferPos = 0;
                _bufferSize = default;
                _sequenceOffset = globalOffset;
            }
            else
            {
                throw new NotSupportedException($"Type {typeof(TInput)} is not supported by this constructor");
            }

            Session = session;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Reader(ReadOnlySpan<byte> input, SerializerSession session, long globalOffset)
        {
            if (IsSpanInput)
            {
                _input = default!;
                _currentSpan = input;
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
                _sequenceOffset = globalOffset;
            }
            else
            {
                throw new NotSupportedException($"Type {typeof(TInput)} is not supported by this constructor");
            }

            Session = session;
        }

        /// <summary>
        /// 获得连环机会话。
        /// </summary>
        /// <value>序列器会话。</value>
        public SerializerSession Session { get; }

        /// <summary>
        /// 获取当前读者位置 。
        /// </summary>
        /// <value>现有位置。</value>
        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (IsReadOnlySequenceInput)
                {
                    var previousBuffersSize = Unsafe.As<TInput, ReadOnlySequenceInput>(ref _input).PreviousBuffersSize;
                    return _sequenceOffset + previousBuffersSize + _bufferPos;
                }
                else if (IsBufferSliceInput)
                {
                    var previousBuffersSize = Unsafe.As<TInput, BufferSliceReaderInput>(ref _input).PreviousBuffersSize;
                    return _sequenceOffset + previousBuffersSize + _bufferPos;
                }
                else if (IsArcBufferInput)
                {
                    var previousBuffersSize = Unsafe.As<TInput, ArcBufferReaderInput>(ref _input).PreviousBuffersSize;
                    return _sequenceOffset + previousBuffersSize + _bufferPos;
                }
                else if (IsSpanInput)
                {
                    return _sequenceOffset + _bufferPos;
                }
                else if (_input is ReaderInput readerInput)
                {
                    return readerInput.Position;
                }
                else
                {
                    return ThrowNotSupportedInput<long>();
                }
            }
        }

        /// <summary>
        /// 输入长度。
        /// </summary>
        /// <value>输入长度。</value>
        public long Length
        {
            get
            {
                if (IsReadOnlySequenceInput)
                {
                    return Unsafe.As<TInput, ReadOnlySequenceInput>(ref _input).Sequence.Length;
                }
                else if (IsBufferSliceInput)
                {
                    return Unsafe.As<TInput, BufferSliceReaderInput>(ref _input).Length;
                }
                else if (IsArcBufferInput)
                {
                    return Unsafe.As<TInput, ArcBufferReaderInput>(ref _input).Length;
                }
                else if (IsSpanInput)
                {
                    return _currentSpan.Length;
                }
                else if (_input is ReaderInput readerInput)
                {
                    return readerInput.Length;
                }
                else
                {
                    return ThrowNotSupportedInput<long>();
                }
            }
        }

        /// <summary>
        /// 获取输入中的未读字节数。
        /// </summary>
        public long Remaining
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (IsReadOnlySequenceInput)
                {
                    ref var input = ref Unsafe.As<TInput, ReadOnlySequenceInput>(ref _input);
                    return input.Sequence.Length - input.PreviousBuffersSize - _bufferPos;
                }
                else if (IsBufferSliceInput)
                {
                    ref var input = ref Unsafe.As<TInput, BufferSliceReaderInput>(ref _input);
                    return input.Length - input.PreviousBuffersSize - _bufferPos;
                }
                else if (IsArcBufferInput)
                {
                    ref var input = ref Unsafe.As<TInput, ArcBufferReaderInput>(ref _input);
                    return input.Length - input.PreviousBuffersSize - _bufferPos;
                }
                else if (IsSpanInput)
                {
                    return _currentSpan.Length - _bufferPos;
                }
                else if (_input is ReaderInput readerInput)
                {
                    return readerInput is StreamReaderInput streamInput ? streamInput.Remaining : readerInput.Length - readerInput.Position;
                }
                else
                {
                    return ThrowNotSupportedInput<long>();
                }
            }
        }

        /// <summary>
        /// 如果输入中保留小于 <paramref name="count"/> 字节的投弃。
        /// </summary>
        /// <param name="count">所需字节数。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureAvailable(uint count)
        {
            var remaining = Remaining;
            if (count > remaining)
            {
                ThrowInvalidSizeException(count, remaining);
            }
        }

        /// <summary>
        /// 读取字节为独立拥有的 <see cref="ArcBuffer"/>。
        /// </summary>
        /// <param name="length">要读取的字节数。</param>
        /// <returns>电话人必须处置的自有缓冲,或 <see cref="ArcBuffer.Empty"/> 零长为。</returns>
        /// <remarks>
        /// 向读者提供 <paramref name="length"/> 字元。 从 Arc 输入的非空读取后, 获取 a
        /// 独立页面上页;其他非空白的读取将字节复制到自有集合页面。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> 为负。</exception>
        /// <exception cref="IndexOutOfRangeException">输入含有少于 <paramref name="length"/> 未读字节。</exception>
        public ArcBuffer ReadOwnedBuffer(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            EnsureAvailable((uint)length);
            if (length == 0) return ArcBuffer.Empty;

            if (IsArcBufferInput)
            {
                ref var input = ref Unsafe.As<TInput, ArcBufferReaderInput>(ref _input);
                var result = input.Slice(_bufferPos, length);
                Skip(length);
                return result;
            }

            var output = new ArcBufferWriter();
            try
            {
                ReadBytes(ref output, length);
                return output.ConsumeSlice(output.Length);
            }
            finally
            {
                output.Dispose();
            }
        }

        /// <summary>
        /// 跳过指定的字节数 。
        /// </summary>
        /// <param name="count">要跳过的字节数。</param>
        public void Skip(long count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than or equal to 0.");
            }

            if (count == 0)
            {
                return;
            }

            if (count > Remaining)
            {
                ThrowInsufficientData();
            }

            if (IsReadOnlySequenceInput)
            {
                var end = Position - _sequenceOffset + count;
                while (Position - _sequenceOffset < end)
                {
                    var previousBuffersSize = Unsafe.As<TInput, ReadOnlySequenceInput>(ref _input).PreviousBuffersSize;
                    if (end - previousBuffersSize <= _bufferSize)
                    {
                        _bufferPos = (int)(end - previousBuffersSize);
                    }
                    else
                    {
                        MoveNext();
                    }
                }
            }
            else if (IsBufferSliceInput)
            {
                var end = Position - _sequenceOffset + count;
                while (Position - _sequenceOffset < end)
                {
                    var previousBuffersSize = Unsafe.As<TInput, BufferSliceReaderInput>(ref _input).PreviousBuffersSize;
                    if (end - previousBuffersSize <= _bufferSize)
                    {
                        _bufferPos = (int)(end - previousBuffersSize);
                    }
                    else
                    {
                        MoveNext();
                    }
                }
            }
            else if (IsArcBufferInput)
            {
                var end = Position - _sequenceOffset + count;
                while (Position - _sequenceOffset < end)
                {
                    var previousBuffersSize = Unsafe.As<TInput, ArcBufferReaderInput>(ref _input).PreviousBuffersSize;
                    if (end - previousBuffersSize <= _bufferSize)
                    {
                        _bufferPos = (int)(end - previousBuffersSize);
                    }
                    else
                    {
                        MoveNext();
                    }
                }
            }
            else if (IsSpanInput)
            {
                _bufferPos += (int)count;
            }
            else if (_input is ReaderInput input)
            {
                input.Skip(count);
            }
            else
            {
                ThrowNotSupportedInput();
            }
        }

        /// <summary>
        /// 创建从指定位置开始的新读取器。
        /// </summary>
        /// <param name="position">
        /// 输入流到叉口的位置。
        /// </param>
        /// <param name="forked">
        /// 预言阅读器的例子。
        /// </param>
        public void ForkFrom(long position, out Reader<TInput> forked)
        {
            if (IsReadOnlySequenceInput)
            {
                ref var typedInput = ref Unsafe.As<TInput, ReadOnlySequenceInput>(ref _input);
                var slicedSequence = typedInput.Sequence.Slice(position - _sequenceOffset);
                var newInput = new ReadOnlySequenceInput
                {
                    Sequence = slicedSequence
                };
                forked = new Reader<TInput>(Unsafe.As<ReadOnlySequenceInput, TInput>(ref newInput), Session, position);

                if (forked.Position != position)
                {
                    ThrowInvalidPosition(position, forked.Position);
                }
            }
            else if (IsBufferSliceInput)
            {
                ref var input = ref Unsafe.As<TInput, BufferSliceReaderInput>(ref _input);
                var newInput = input.ForkFrom(checked((int)position));
                forked = new Reader<TInput>(Unsafe.As<BufferSliceReaderInput, TInput>(ref newInput), Session, position);

                if (forked.Position != position)
                {
                    ThrowInvalidPosition(position, forked.Position);
                }
            }
            else if (IsArcBufferInput)
            {
                ref var input = ref Unsafe.As<TInput, ArcBufferReaderInput>(ref _input);
                var newInput = input.ForkFrom(checked((int)(position - _sequenceOffset)));
                forked = new Reader<TInput>(Unsafe.As<ArcBufferReaderInput, TInput>(ref newInput), Session, position);

                if (forked.Position != position)
                {
                    ThrowInvalidPosition(position, forked.Position);
                }
            }
            else if (IsSpanInput)
            {
                forked = new Reader<TInput>(_currentSpan[(int)position..], Session, position);
                if (forked.Position != position || position > int.MaxValue)
                {
                    ThrowInvalidPosition(position, forked.Position);
                }
            }
            else if (_input is ReaderInput input)
            {
                input.Seek(position);
                forked = new Reader<TInput>(_input, Session, 0);

                if (forked.Position != position)
                {
                    ThrowInvalidPosition(position, forked.Position);
                }
            }
            else
            {
                throw new NotSupportedException($"Type {typeof(TInput)} is not supported");
            }

            static void ThrowInvalidPosition(long expectedPosition, long actualPosition)
            {
                throw new InvalidOperationException($"Expected to arrive at position {expectedPosition} after ForkFrom, but resulting position is {actualPosition}");
            }
        }

        /// <summary>
        /// 将预告阅读器不再使用后, 读者从指定位置上恢复到已取消的读取器 。
        /// </summary>
        /// <param name="position">
        /// 恢复读取的位置 。
        /// </param>
        public void ResumeFrom(long position)
        {
            if (IsReadOnlySequenceInput)
            {
                // 不需要任何要求。
            }
            else if (IsBufferSliceInput)
            {
                // 不需要任何要求。
            }
            else if (IsArcBufferInput)
            {
                // 不需要任何要求。
            }
            else if (IsSpanInput)
            {
                // 不需要任何要求。
            }
            else if (_input is ReaderInput input)
            {
                // 搜索输入流 。
                input.Seek(Position);
            }
            else
            {
                throw new NotSupportedException($"Type {typeof(TInput)} is not supported");
            }

            if (position != Position)
            {
                ThrowInvalidPosition(position, Position);
            }

            static void ThrowInvalidPosition(long expectedPosition, long actualPosition)
            {
                throw new InvalidOperationException($"Expected to arrive at position {expectedPosition} after ResumeFrom, but resulting position is {actualPosition}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void MoveNext()
        {
            if (IsReadOnlySequenceInput)
            {
                ref var typedInput = ref Unsafe.As<TInput, ReadOnlySequenceInput>(ref _input);
                typedInput.PreviousBuffersSize += _bufferSize;

                // 如果这是第一个调用 MoveNext , 那么 nextSequencePosition 无效, 必须移到第二个位置 。
                if (typedInput.NextSequencePosition.Equals(typedInput.Sequence.Start))
                {
                    _ = typedInput.Sequence.TryGet(ref typedInput.NextSequencePosition, out _);
                }

                if (!typedInput.Sequence.TryGet(ref typedInput.NextSequencePosition, out var memory))
                {
                    _currentSpan = memory.Span;
                    ThrowInsufficientData();
                }

                _currentSpan = memory.Span;
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
            }
            else if (IsBufferSliceInput)
            {
                ref var slice = ref Unsafe.As<TInput, BufferSliceReaderInput>(ref _input);
                slice.PreviousBuffersSize += _bufferSize;
                _currentSpan = slice.GetNext();
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
            }
            else if (IsArcBufferInput)
            {
                ref var slice = ref Unsafe.As<TInput, ArcBufferReaderInput>(ref _input);
                slice.PreviousBuffersSize += _bufferSize;
                _currentSpan = slice.GetNext();
                _bufferPos = 0;
                _bufferSize = _currentSpan.Length;
            }
            else if (IsSpanInput)
            {
                ThrowInsufficientData();
            }
            else
            {
                ThrowNotSupportedInput();
            }
        }

        /// <summary>
        /// 读取输入中的字节。
        /// </summary>
        /// <returns>读到的字节</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                var pos = _bufferPos;
                if ((uint)pos < (uint)_currentSpan.Length)
                {
                    // https://github.com/dotnet/runtime/issues/72004
                    var result = Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), (uint)pos);
                    _bufferPos = pos + 1;
                    return result;
                }

                return ReadByteSlow(ref this);
            }
            else if (_input is ReaderInput readerInput)
            {
                return readerInput.ReadByte();
            }
            else
            {
                return ThrowNotSupportedInput<byte>();
            }
        }

        /// <summary>
        /// 返回输入的下一个字节,而不向读者推进。
        /// </summary>
        /// <returns>输入中的下一个字节。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte PeekByte()
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                var reader = this;
                return reader.ReadByte();
            }
            else if (_input is ReaderInput readerInput)
            {
                var position = readerInput.Position;
                try
                {
                    return readerInput.ReadByte();
                }
                finally
                {
                    readerInput.Seek(position);
                }
            }
            else
            {
                return ThrowNotSupportedInput<byte>();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte ReadByteSlow(ref Reader<TInput> reader)
        {
            reader.MoveNext();
            return reader._currentSpan[reader._bufferPos++];
        }

        /// <summary>
        /// 从输入读取 <see cref="int"/> 。
        /// </summary>
        /// <returns>下 <see cref="int"/> 后经宣读。</returns>
        public int ReadInt32() => (int)ReadUInt32();

        /// <summary>
        /// 从输入读取 <see cref="uint"/> 。
        /// </summary>
        /// <returns>下 <see cref="uint"/> 后经宣读。</returns>
        public uint ReadUInt32()
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                const int width = 4;
                if (_bufferPos + width > _bufferSize)
                {
                    return ReadSlower(ref this);
                }

                var result = BinaryPrimitives.ReadUInt32LittleEndian(_currentSpan.Slice(_bufferPos, width));
                _bufferPos += width;
                return result;

                static uint ReadSlower(ref Reader<TInput> r)
                {
                    uint b1 = r.ReadByte();
                    uint b2 = r.ReadByte();
                    uint b3 = r.ReadByte();
                    uint b4 = r.ReadByte();

                    return b1 | (b2 << 8) | (b3 << 16) | (b4 << 24);
                }
            }
            else if (_input is ReaderInput readerInput)
            {
                return readerInput.ReadUInt32();
            }
            else
            {
                return ThrowNotSupportedInput<uint>();
            }
        }

        /// <summary>
        /// 从输入读取 <see cref="long"/> 。
        /// </summary>
        /// <returns>下 <see cref="long"/> 后经宣读。</returns>
        public long ReadInt64() => (long)ReadUInt64();

        /// <summary>
        /// 从输入读取 <see cref="ulong"/> 。
        /// </summary>
        /// <returns>下 <see cref="ulong"/> 后经宣读。</returns>
        public ulong ReadUInt64()
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                const int width = 8;
                if (_bufferPos + width > _bufferSize)
                {
                    return ReadSlower(ref this);
                }

                var result = BinaryPrimitives.ReadUInt64LittleEndian(_currentSpan.Slice(_bufferPos, width));
                _bufferPos += width;
                return result;

                static ulong ReadSlower(ref Reader<TInput> r)
                {
                    ulong b1 = r.ReadByte();
                    ulong b2 = r.ReadByte();
                    ulong b3 = r.ReadByte();
                    ulong b4 = r.ReadByte();
                    ulong b5 = r.ReadByte();
                    ulong b6 = r.ReadByte();
                    ulong b7 = r.ReadByte();
                    ulong b8 = r.ReadByte();

                    return b1 | (b2 << 8) | (b3 << 16) | (b4 << 24)
                           | (b5 << 32) | (b6 << 40) | (b7 << 48) | (b8 << 56);
                }
            }
            else if (_input is ReaderInput readerInput)
            {
                return readerInput.ReadUInt64();
            }
            else
            {
                return ThrowNotSupportedInput<uint>();
            }
        }

        [DoesNotReturn]
        private static void ThrowInsufficientData() => throw new InvalidOperationException("Insufficient data present in buffer.");

        /// <summary>
        /// 读取给提供作家指定的字节数 。
        /// </summary>
        public void ReadBytes<TBufferWriter>(scoped ref TBufferWriter writer, int count) where TBufferWriter : IBufferWriter<byte>
        {
            if (count < 0)
            {
                ThrowArgumentOutOfRangeException(count);
            }

            EnsureAvailable((uint)count);

            int chunkSize;
            for (var remaining = count; remaining > 0; remaining -= chunkSize)
            {
                var span = writer.GetSpan();
                if (span.Length > remaining)
                {
                    span = span[..remaining];
                }

                ReadBytes(span);
                chunkSize = span.Length;
                writer.Advance(chunkSize);
            }
        }

        /// <summary>
        /// 从输入中读取字节数组。
        /// </summary>
        /// <param name="count">要读取的数组长度。</param>
        /// <returns>数组 wihch 已经读取。</returns>
        public byte[] ReadBytes(uint count)
        {
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            EnsureAvailable(count);

            var bytes = new byte[count];
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                var destination = new Span<byte>(bytes);
                ReadBytes(destination);
            }
            else if (_input is ReaderInput readerInput)
            {
                readerInput.ReadBytes(bytes, 0, (int)count);
            }

            return bytes;
        }

        /// <summary>
        /// 填充 <paramref name="destination"/> ,从输入中读取字节。
        /// </summary>
        /// <param name="destination">目的地</param>
        public void ReadBytes(scoped Span<byte> destination)
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                if (_bufferPos + destination.Length <= _bufferSize)
                {
                    _currentSpan.Slice(_bufferPos, destination.Length).CopyTo(destination);
                    _bufferPos += destination.Length;
                    return;
                }

                ReadBytesMultiSegment(destination);
            }
            else if (_input is ReaderInput readerInput)
            {
                readerInput.ReadBytes(destination);
            }
            else
            {
                ThrowNotSupportedInput();
            }
        }

        private void ReadBytesMultiSegment(scoped Span<byte> dest)
        {
            while (true)
            {
                var writeSize = Math.Min(dest.Length, _currentSpan.Length - _bufferPos);
                _currentSpan.Slice(_bufferPos, writeSize).CopyTo(dest);
                _bufferPos += writeSize;
                dest = dest[writeSize..];

                if (dest.Length == 0)
                {
                    break;
                }

                MoveNext();
            }
        }

        /// <summary>
        /// 从输入中输入指定字节数的读取。
        /// </summary>
        /// <param name="length">长度。</param>
        /// <param name="bytes">读取的字节 。</param>
        /// <returns><see langword="true"/> ,如果从输入中读取了指定的字节数量,则 <see langword="false"/> 。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadBytes(int length, out ReadOnlySpan<byte> bytes)
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                if (_bufferPos + length <= _bufferSize)
                {
                    bytes = _currentSpan.Slice(_bufferPos, length);
                    _bufferPos += length;
                    return true;
                }

                bytes = default;
                return false;
            }
            else if (_input is ReaderInput readerInput)
            {
                return readerInput.TryReadBytes(length, out bytes);
            }
            else
            {
                bytes = default;
                return ThrowNotSupportedInput<bool>();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal uint ReadVarUInt32NoInlining() => ReadVarUInt32();

        /// <summary>
        /// 从输入中读取变量宽 <see cref="uint"/> 。
        /// </summary>
        /// <returns>下 <see cref="uint"/> 后经宣读。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe uint ReadVarUInt32()
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                var pos = _bufferPos;

                if (!BitConverter.IsLittleEndian || pos + 8 > _currentSpan.Length)
                {
                    return ReadVarUInt32Slow();
                }

                // msb 位置的零数决定了要读取的字节数。
                // 64 位整数时最多可达10。
                ref byte readHead = ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos);

                ulong result = Unsafe.ReadUnaligned<ulong>(ref readHead);
                var bytesNeeded = BitOperations.TrailingZeroCount((uint)result) + 1;
                if (bytesNeeded > 5) ThrowOverflowException();
                _bufferPos = pos + bytesNeeded;
                result &= (1UL << (bytesNeeded * 8)) - 1;
                result >>= bytesNeeded;
                return checked((uint)result);
            }
            else
            {
                return ReadVarUInt32Slow();
            }
        }

        private static void ThrowOverflowException() => throw new OverflowException();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private uint ReadVarUInt32Slow()
        {
            var header = ReadByte();
            var numBytes = Orleans.Serialization.Buffers.Reader.GetVarIntByteCount(header);

            // 5字节案件 宽到乌隆
            ulong result = header;

            // 视需要读取额外字节
            var shiftBy = 8;
            var i = numBytes;
            while (--i > 0)
            {
                result |= (ulong)ReadByte() << shiftBy;
                shiftBy += 8;
            }

            result >>= numBytes;
            return checked((uint)result);
        }

        /// <summary>
        /// 从输入中读取变量宽 <see cref="ulong"/> 。
        /// </summary>
        /// <returns>下 <see cref="ulong"/> 后经宣读。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadVarUInt64()
        {
            if (IsReadOnlySequenceInput || IsSpanInput || IsBufferSliceInput || IsArcBufferInput)
            {
                var pos = _bufferPos;

                if (!BitConverter.IsLittleEndian || pos + 10 > _currentSpan.Length)
                {
                    return ReadVarUInt64Slow();
                }

                // msb 位置的零数决定了要读取的字节数。
                // 32位整数最高为 5。
                ref byte readHead = ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos);

                ulong result = Unsafe.ReadUnaligned<ulong>(ref readHead);

                var bytesNeeded = BitOperations.TrailingZeroCount(result) + 1;
                if (bytesNeeded > 10)
                {
                    ThrowOverflowException();
                }

                result >>= bytesNeeded;
                _bufferPos += bytesNeeded;

                ushort upper = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref readHead, sizeof(ulong)));
                if (bytesNeeded == 10 && (upper & 0xFC00) != 0)
                {
                    ThrowOverflowException();
                }

                result |= ((ulong)upper) << (64 - bytesNeeded);

                if (bytesNeeded < 10)
                {
                    result &= (1UL << (bytesNeeded * 7)) - 1;
                }

                return result;
            }
            else
            {
                return ReadVarUInt64Slow();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ulong ReadVarUInt64Slow()
        {
            var header = ReadByte();
            var numBytes = Orleans.Serialization.Buffers.Reader.GetVarIntByteCount(header);

            // 5字节案件 宽到乌隆
            ulong result = header;

            // 视需要读取额外字节
            if (numBytes < 9)
            {
                var shiftBy = 8;
                var i = numBytes;
                while (--i > 0)
                {
                    result |= (ulong)ReadByte() << shiftBy;
                    shiftBy += 8;
                }

                result >>= numBytes;
                return result;
            }
            else
            {
                result |= (ulong)ReadByte() << 8;

                // 如果有一个以上的字节值 的尾数零, 现在再读一遍,因为我们有 更多的数据。
                numBytes = BitOperations.TrailingZeroCount(result) + 1;

                if (numBytes == 9)
                {
                    result |= (ulong)ReadByte() << 16;
                    result |= (ulong)ReadByte() << 24;
                    result |= (ulong)ReadByte() << 32;

                    result |= (ulong)ReadByte() << 40;
                    result |= (ulong)ReadByte() << 48;
                    result |= (ulong)ReadByte() << 56;
                    result >>= 9;

                    var upper = (ushort)ReadByte();
                    result |= ((ulong)upper) << (64 - 9);
                    return result;
                }
                else if (numBytes == 10)
                {
                    result |= (ulong)ReadByte() << 16;
                    result |= (ulong)ReadByte() << 24;
                    result |= (ulong)ReadByte() << 32;

                    result |= (ulong)ReadByte() << 40;
                    result |= (ulong)ReadByte() << 48;
                    result |= (ulong)ReadByte() << 56;
                    result >>= 10;

                    var upper = (ushort)(ReadByte() | (ushort)(ReadByte() << 8));
                    if ((upper & 0xFC00) != 0)
                    {
                        ThrowOverflowException();
                    }

                    result |= ((ulong)upper) << (64 - 10);
                    return result;
                }
            }

            return ExceptionHelper.ThrowArgumentOutOfRange<ulong>("value");
        }

        private static T ThrowNotSupportedInput<T>() => throw new NotSupportedException($"Type {typeof(TInput)} is not supported");

        private static void ThrowNotSupportedInput() => throw new NotSupportedException($"Type {typeof(TInput)} is not supported");

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidSizeException(uint length, long remaining) => throw new IndexOutOfRangeException(
            $"Declared length, {length}, is greater than the remaining length of the input, {remaining}.");

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgumentOutOfRangeException(int count) => throw new ArgumentOutOfRangeException(
            nameof(count), count, "The count must not be negative.");
    }
}
