using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
#if NETCOREAPP3_1_OR_GREATER
using System.Numerics;
#else
using Orleans.Serialization.Utilities;
#endif
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Session;

namespace Orleans.Serialization.Buffers
{
    /// <summary>
    /// 创建 <see cref="Writer{TBufferWriter}"/> 示例的帮助器方法。
    /// </summary>
    public static class Writer
    {
        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写器输出类型。</typeparam>
        /// <param name="destination">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<TBufferWriter> Create<TBufferWriter>(TBufferWriter destination, SerializerSession session) where TBufferWriter : IBufferWriter<byte> => new(destination, session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="destination">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<ArcBufferWriterWrapper> Create(ArcBufferWriter destination, SerializerSession session) => new(new(destination), session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="destination">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<MemoryStreamBufferWriter> Create(MemoryStream destination, SerializerSession session) => new(new MemoryStreamBufferWriter(destination), session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="destination">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <param name="sizeHint">大小提示。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<PoolingStreamBufferWriter> CreatePooled(Stream destination, SerializerSession session, int sizeHint = 0) => new(new PoolingStreamBufferWriter(destination, sizeHint), session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="destination">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <param name="sizeHint">大小提示。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<ArrayStreamBufferWriter> Create(Stream destination, SerializerSession session, int sizeHint = 0) => new(new ArrayStreamBufferWriter(destination, sizeHint), session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="output">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<SpanBufferWriter> Create(byte[] output, SerializerSession session) => Create(output.AsSpan(), session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="output">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<MemoryBufferWriter> Create(Memory<byte> output, SerializerSession session) => new(new MemoryBufferWriter(output), session);

        /// <summary>
        /// 创建写到指定目的地的作家。
        /// </summary>
        /// <param name="output">目的地</param>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<SpanBufferWriter> Create(Span<byte> output, SerializerSession session) => new(new SpanBufferWriter(output), output, session);

        /// <summary>
        /// 创建写作器, 将其写入集合缓冲 。
        /// </summary>
        /// <param name="session">本届会议。</param>
        /// <returns>一个新的 <see cref="Writer{TBufferWriter}"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Writer<PooledBuffer> CreatePooled(SerializerSession session) => new(new PooledBuffer(), session);
    }

    /// <summary>
    /// 将 <see cref="ArcBufferWriter"/> 包成一个序列化作家目标。
    /// </summary>
    /// <remarks>
    /// 处理此包装纸上的 <see cref="Writer{TBufferWriter}"/> 不处置底部 <see cref="ArcBufferWriter"/>。
    /// </remarks>
    /// <param name="bufferWriter">包装缓冲写作器。</param>
    public readonly struct ArcBufferWriterWrapper(ArcBufferWriter bufferWriter) : IBufferWriter<byte>
    {
        /// <inheritdoc/>
        public void Advance(int count) => ((IBufferWriter<byte>)bufferWriter).Advance(count);

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0) => bufferWriter.GetMemory(sizeHint);

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0) => bufferWriter.GetSpan(sizeHint);
    }

    /// <summary>
    /// 提供将文字写入输出流的功能。
    /// </summary>
    /// <typeparam name="TBufferWriter">后含缓冲写作类型。</typeparam>
    public ref partial struct Writer<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
    {
        /// <summary>
        /// 输出缓冲写器
        /// </summary>
        /// <remarks>
        /// 直接修改输出可能会腐蚀作者的状态。
        /// </remarks>
        public TBufferWriter Output;

        /// <summary>
        /// 当前写字间隔 。
        /// </summary>
        private Span<byte> _currentSpan;

        /// <summary>
        /// 在当前范围内的缓冲位置。
        /// </summary>
        private int _bufferPos;

        /// <summary>
        /// 上次缓冲的大小 。
        /// </summary>
        private int _previousBuffersSize;

        /// <summary>
        /// 最大段缓冲大小提示(1MB)
        /// </summary>
        internal const int MaxMultiSegmentSizeHint = 1024 * 1024;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Writer(TBufferWriter output, SerializerSession session)
        {
            Debug.Assert(output is not SpanBufferWriter);
            Output = output;
            Session = session;
            _currentSpan = Output.GetSpan();
            _bufferPos = default;
            _previousBuffersSize = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Writer(TBufferWriter output, Span<byte> span, SerializerSession session)
        {
            Debug.Assert(output is SpanBufferWriter);
            Output = output;
            Session = session;
            _currentSpan = span;
            _bufferPos = default;
            _previousBuffersSize = default;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            // 避免打斗, 更好的渗透器和代码。
            if (typeof(TBufferWriter).IsValueType)
            {
                if (Output is IDisposable)
                {
                    ((IDisposable)Output).Dispose();
                }
            }
            else
            {
                (Output as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// 获得连环机会话。
        /// </summary>
        /// <value>序列器会话。</value>
        public SerializerSession Session { get; }

        /// <summary>
        /// 获得位置。
        /// </summary>
        /// <value>位置。</value>
        public readonly int Position => _previousBuffersSize + _bufferPos;

        /// <summary>
        /// 获取当前可写频谱。
        /// </summary>
        /// <value>当前可写字的间隔。</value>
        public readonly Span<byte> WritableSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentSpan[_bufferPos..];
        }

        /// <summary>
        /// 在当前间隔内推进写入位置。
        /// </summary>
        /// <param name="length">用于预发 wirte 位置的字节数。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AdvanceSpan(int length) => _bufferPos += length;

        /// <summary>
        /// 提交当前写入的缓冲文件。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Commit()
        {
            Output.Advance(_bufferPos);

            if (!typeof(TBufferWriter).IsValueType || typeof(TBufferWriter) != typeof(SpanBufferWriter))
            {
                _previousBuffersSize += _bufferPos;
                _currentSpan = default;
                _bufferPos = default;
            }
        }

        /// <summary>
        /// 确保至少有 <paramref name="length"/> 毗连字节可以写入。
        /// </summary>
        /// <param name="length">要确保的毗连字节数。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureContiguous(int length)
        {
            // 目前缓冲剂足够。
            if (_bufferPos + length <= _currentSpan.Length)
            {
                return;
            }

            // 目前的缓冲不够, 另分配一个。
            Allocate(length);
#if DEBUG
            // 如果分配未满足请求, 则丢弃 。
            if (_currentSpan.Length < length)
                throw new InvalidOperationException($"Requested buffer length {length} cannot be satisfied by the writer.");
#endif
        }

        /// <summary>
        /// 为指定的字节数分配缓冲空间。
        /// </summary>
        /// <param name="sizeHint">准备储备的字节数。</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Allocate(int sizeHint)
        {
            // 提交已写入的字节 。
            Output.Advance(_bufferPos);

            // 请求新建至少包含所需可用字节数的缓冲文件 。
            _currentSpan = Output.GetSpan(sizeHint);

            // 更新新缓冲的内建状态 。
            _previousBuffersSize += _bufferPos;
            _bufferPos = 0;
        }

        /// <summary>
        /// 写入指定值。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(scoped ReadOnlySpan<byte> value)
        {
            // 快路径, 尝试复制到当前缓冲 。
            var destination = WritableSpan;
            if ((uint)value.Length <= (uint)destination.Length)
            {
                value.CopyTo(destination);
                _bufferPos += value.Length;
            }
            else
            {
                WriteMultiSegment(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteMultiSegment(scoped ReadOnlySpan<byte> input)
        {
            while (true)
            {
                // 尽可能/必要地写入当前段。
                var span = WritableSpan;
                var writeSize = Math.Min(span.Length, input.Length);
                input[..writeSize].CopyTo(span);
                _bufferPos += writeSize;

                input = input[writeSize..];

                if (input.Length == 0)
                {
                    return;
                }

                // 当前段满载, 但还有更多要写 。
                Allocate(Math.Min(input.Length, MaxMultiSegmentSizeHint));
            }
        }

        /// <summary>
        /// 将提供的 <see cref="byte"/> 写入输出缓冲。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            nuint bufferPos = (uint)_bufferPos;
            if ((uint)bufferPos < (uint)_currentSpan.Length)
            {
                // https://github.com/dotnet/runtime/issues/72004
                Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), bufferPos) = value;
                _bufferPos = (int)(uint)bufferPos + 1;
            }
            else
            {
                WriteByteSlow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteByteSlow(byte value)
        {
            Allocate(1);
            _currentSpan[0] = value;
            _bufferPos = 1;
        }

        /// <summary>
        /// 将提供的 <see cref="int"/> 写入输出缓冲。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value) => WriteUInt32((uint)value);

        /// <summary>
        /// 将提供的 <see cref="long"/> 写入输出缓冲。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value) => WriteUInt64((ulong)value);

        /// <summary>
        /// 将提供的 <see cref="uint"/> 写入输出缓冲。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            nuint pos = (uint)_bufferPos;
            int newPos = (int)(uint)pos + sizeof(uint);
            if ((uint)newPos <= (uint)_currentSpan.Length)
            {
                _bufferPos = newPos;
                if (!BitConverter.IsLittleEndian) value = BinaryPrimitives.ReverseEndianness(value);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), value);
            }
            else
            {
                WriteUInt32Slow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteUInt32Slow(uint value)
        {
            Allocate(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(_currentSpan, value);
            _bufferPos = sizeof(uint);
        }

        /// <summary>
        /// 将提供的 <see cref="ulong"/> 写入输出缓冲。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
        {
            nuint pos = (uint)_bufferPos;
            int newPos = (int)(uint)pos + sizeof(ulong);
            if ((uint)newPos <= (uint)_currentSpan.Length)
            {
                _bufferPos = newPos;
                if (!BitConverter.IsLittleEndian) value = BinaryPrimitives.ReverseEndianness(value);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), value);
            }
            else
            {
                WriteUInt64Slow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteUInt64Slow(ulong value)
        {
            Allocate(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(_currentSpan, value);
            _bufferPos = sizeof(ulong);
        }

        /// <summary>
        /// 将一个7位数的无符号值作为变量-宽整数写入。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteVarUInt7(uint value)
        {
            Debug.Assert(value < 1 << 7);
            WriteByte((byte)((value << 1) + 1));
        }

        /// <summary>
        /// 将一个28位未符号值作为变量-宽整数写入。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteVarUInt28(uint value)
        {
            Debug.Assert(value < 1 << 28);

            var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);

            uint lower = ((value << 1) + 1) << neededBytes;

            nuint pos = (uint)_bufferPos;
            if ((uint)pos + sizeof(uint) <= (uint)_currentSpan.Length)
            {
                _bufferPos = (int)(uint)pos + neededBytes + 1;
                if (!BitConverter.IsLittleEndian) lower = BinaryPrimitives.ReverseEndianness(lower);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), lower);
            }
            else
            {
                WriteVarUInt28Slow(lower);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteVarUInt28Slow(uint lower)
        {
            Allocate(sizeof(uint));

            var neededBytes = BitOperations.TrailingZeroCount(lower) + 1;
            BinaryPrimitives.WriteUInt32LittleEndian(_currentSpan, lower);
            _bufferPos = neededBytes;
        }

        /// <summary>
        /// 将提供的 <see cref="uint"/> 写入输出缓冲,作为变量-宽整数。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt32(uint value)
        {
            var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);

            ulong lower = (((ulong)value << 1) + 1) << neededBytes;

            nuint pos = (uint)_bufferPos;
            if ((uint)pos + sizeof(ulong) <= (uint)_currentSpan.Length)
            {
                _bufferPos = (int)(uint)pos + neededBytes + 1;
                if (!BitConverter.IsLittleEndian) lower = BinaryPrimitives.ReverseEndianness(lower);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), lower);
            }
            else
            {
                WriteVarUInt56Slow(lower);
            }
        }

        /// <summary>
        /// 将56位未符号值作为变量-宽整数写入。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteVarUInt56(ulong value)
        {
            Debug.Assert(value < 1UL << 56);

            var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);

            ulong lower = ((value << 1) + 1) << neededBytes;

            nuint pos = (uint)_bufferPos;
            if ((uint)pos + sizeof(ulong) <= (uint)_currentSpan.Length)
            {
                _bufferPos = (int)(uint)pos + neededBytes + 1;
                if (!BitConverter.IsLittleEndian) lower = BinaryPrimitives.ReverseEndianness(lower);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos), lower);
            }
            else
            {
                WriteVarUInt56Slow(lower);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteVarUInt56Slow(ulong lower)
        {
            Allocate(sizeof(ulong));

            var neededBytes = BitOperations.TrailingZeroCount((uint)lower) + 1;
            BinaryPrimitives.WriteUInt64LittleEndian(_currentSpan, lower);
            _bufferPos = neededBytes;
        }

        /// <summary>
        /// 将提供的 <see cref="ulong"/> 写入输出缓冲,作为变量-宽整数。
        /// </summary>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt64(ulong value)
        {
            nuint pos = (uint)_bufferPos;
            // 因为这方法写了一块乌隆加一个粗略值的字节,
            if ((uint)pos + sizeof(ulong) + sizeof(ushort) <= (uint)_currentSpan.Length)
            {
                var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);
                _bufferPos = (int)(uint)pos + neededBytes + 1;

                ulong lower = ((value << 1) + 1) << neededBytes;

                ref var writeHead = ref Unsafe.Add(ref MemoryMarshal.GetReference(_currentSpan), pos);
                if (!BitConverter.IsLittleEndian) lower = BinaryPrimitives.ReverseEndianness(lower);
                Unsafe.WriteUnaligned(ref writeHead, lower);

                // 无条件写2字节溢出
                var upper = value >> (63 - neededBytes);
                writeHead = ref Unsafe.Add(ref writeHead, sizeof(ulong));
                if (!BitConverter.IsLittleEndian) upper = BinaryPrimitives.ReverseEndianness((ushort)upper);
                Unsafe.WriteUnaligned(ref writeHead, (ushort)upper);
            }
            else
            {
                WriteVarUInt64Slow(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteVarUInt64Slow(ulong value)
        {
            Allocate(sizeof(ulong) + sizeof(ushort));

            var neededBytes = (int)((uint)BitOperations.Log2(value) / 7);
            _bufferPos = neededBytes + 1;

            ulong lower = ((value << 1) + 1) << neededBytes;
            BinaryPrimitives.WriteUInt64LittleEndian(_currentSpan, lower);

            // 无条件写2字节溢出
            var upper = value >> (63 - neededBytes);
            BinaryPrimitives.WriteUInt16LittleEndian(_currentSpan[sizeof(ulong)..], (ushort)upper);
        }
    }
}
