using System;
using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="BitArray"/> 数组的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed partial class BitArrayCodec : IFieldCodec<BitArray>
    {
#if !NET10_0_OR_GREATER
        private static int[] GetSetArray(BitArray bitArray) => (typeof(BitArray).GetField("m_array", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(bitArray) as int[])!;
#endif

        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        BitArray IFieldCodec<BitArray>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static BitArray ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<BitArray, TInput>(ref reader, field);
            }

            field.EnsureWireType(WireType.LengthPrefixed);
            var numBytes = reader.ReadVarUInt32();
            reader.EnsureAvailable(numBytes);
            if (numBytes > int.MaxValue / 8)
            {
                ThrowInvalidSizeException(numBytes);
            }

            var result = new BitArray((int)numBytes * 8, false);
#if NET10_0_OR_GREATER
            reader.ReadBytes(CollectionsMarshal.AsBytes(result)[..(int)numBytes]);
#else
            var resultArray = GetSetArray(result);
            reader.ReadBytes(MemoryMarshal.AsBytes(resultArray.AsSpan()).Slice(0, (int)numBytes));
#endif

            ReferenceCodec.RecordObject(reader.Session, result);
            return result!;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidSizeException(uint numBytes) => throw new IndexOutOfRangeException(
            $"The declared BitArray length, {numBytes} bytes, exceeds the maximum supported length.");

        void IFieldCodec<BitArray>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] BitArray value)
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(BitArray), WireType.LengthPrefixed);

#if NET10_0_OR_GREATER
            var bytes = CollectionsMarshal.AsBytes(value);
            writer.WriteVarUInt32((uint)bytes.Length);
            writer.Write(bytes);
#else
            var numBytes = GetByteArrayLengthFromBitLength(value.Length);
            writer.WriteVarUInt32((uint)numBytes);
            writer.Write(MemoryMarshal.AsBytes(GetSetArray(value).AsSpan()).Slice(0, numBytes));
#endif

#if !NET10_0_OR_GREATER
            static int GetByteArrayLengthFromBitLength(int n)
            {
                const int BitShiftPerByte = 3;
                Debug.Assert(n >= 0);
                return (int)((uint)(n - 1 + (1 << BitShiftPerByte)) >> BitShiftPerByte);
            }
#endif
        }
    }

    /// <summary>
    /// <see cref="byte"/> 数组的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class BitArrayCopier : IDeepCopier<BitArray>
    {
        /// <inheritdoc/>
        BitArray? IDeepCopier<BitArray>.DeepCopy(BitArray? input, CopyContext context) => DeepCopy(input, context);

        /// <summary>
        /// 创建给定输入的深拷贝。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="context">上下文。</param>
        /// <returns><paramref name="input" /> 的副本。</returns>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public static BitArray? DeepCopy(BitArray? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<BitArray>(input, out var result))
            {
                return result!;
            }

            result = new(input);
            context.RecordCopy(input, result);
            return result;
        }
    }

    /// <summary>
    /// <see cref="byte"/> 数组的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class ByteArrayCodec : IFieldCodec<byte[]>
    {
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        byte[] IFieldCodec<byte[]>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static byte[] ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<byte[], TInput>(ref reader, field);
            }

            field.EnsureWireType(WireType.LengthPrefixed);
            var length = reader.ReadVarUInt32();
            var result = reader.ReadBytes(length);
            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        void IFieldCodec<byte[]>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] byte[] value)
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(byte[]), WireType.LengthPrefixed);
            writer.WriteVarUInt32((uint)value.Length);
            writer.Write(value);
        }

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型在静态分析时已知）。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, byte[] value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceFieldExpected(ref writer, fieldIdDelta, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.LengthPrefixed);
            writer.WriteVarUInt32((uint)value.Length);
            writer.Write(value);
        }
    }

    /// <summary>
    /// <see cref="byte"/> 数组的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class ByteArrayCopier : IDeepCopier<byte[]>
    {
        /// <inheritdoc/>
        byte[]? IDeepCopier<byte[]>.DeepCopy(byte[]? input, CopyContext context) => DeepCopy(input, context);

        /// <summary>
        /// 创建给定输入的深拷贝。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="context">上下文。</param>
        /// <returns><paramref name="input" /> 的副本。</returns>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public static byte[]? DeepCopy(byte[]? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<byte[]>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            result = new byte[input.Length];
            context.RecordCopy(input, result);
            input.CopyTo(result.AsSpan());
            return result;
        }
    }

    /// <summary>
    /// <see cref="ReadOnlyMemory{Byte}"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class ReadOnlyMemoryOfByteCodec : IFieldCodec<ReadOnlyMemory<byte>>
    {
        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        ReadOnlyMemory<byte> IFieldCodec<ReadOnlyMemory<byte>>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static byte[] ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<byte[], TInput>(ref reader, field);
            }

            field.EnsureWireType(WireType.LengthPrefixed);
            var length = reader.ReadVarUInt32();
            var result = reader.ReadBytes(length);
            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        /// <inheritdoc/>
        void IFieldCodec<ReadOnlyMemory<byte>>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] ReadOnlyMemory<byte> value) => WriteField(ref writer, fieldIdDelta, expectedType, value);

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型在静态分析时已知）。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, ReadOnlyMemory<byte> value) where TBufferWriter : IBufferWriter<byte>
            => WriteField(ref writer, fieldIdDelta, typeof(ReadOnlyMemory<byte>), value);

        private static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] ReadOnlyMemory<byte> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(ReadOnlyMemory<byte>), WireType.LengthPrefixed);
            writer.WriteVarUInt32((uint)value.Length);
            writer.Write(value.Span);
        }
    }

    /// <summary>
    /// <see cref="ReadOnlyMemory{Byte}"/> 的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class ReadOnlyMemoryOfByteCopier : IDeepCopier<ReadOnlyMemory<byte>>
    {
        /// <inheritdoc/>
        ReadOnlyMemory<byte> IDeepCopier<ReadOnlyMemory<byte>>.DeepCopy(ReadOnlyMemory<byte> input, CopyContext _) => DeepCopy(input, _);

        /// <summary>
        /// 复制输入。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="copyContext">复制上下文。</param>
        /// <returns>输入的副本。</returns>
        public static ReadOnlyMemory<byte> DeepCopy(ReadOnlyMemory<byte> input, CopyContext copyContext)
        {
            if (input.IsEmpty)
            {
                return default;
            }

            return input.ToArray();
        }
    }

    /// <summary>
    /// <see cref="ArraySegment{Byte}"/> 的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class ArraySegmentOfByteCopier : IDeepCopier<ArraySegment<byte>>
    {
        /// <inheritdoc/>
        ArraySegment<byte> IDeepCopier<ArraySegment<byte>>.DeepCopy(ArraySegment<byte> input, CopyContext _) => DeepCopy(input, _);

        /// <summary>
        /// 复制输入。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="copyContext">复制上下文。</param>
        /// <returns>输入的副本。</returns>
        public static ArraySegment<byte> DeepCopy(ArraySegment<byte> input, CopyContext copyContext)
        {
            if (input.Array is null)
            {
                return default;
            }

            return input.AsSpan().ToArray();
        }
    }

    /// <summary>
    /// <see cref="Memory{Byte}"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class MemoryOfByteCodec : IFieldCodec<Memory<byte>>
    {
        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        Memory<byte> IFieldCodec<Memory<byte>>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static Memory<byte> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<byte[], TInput>(ref reader, field);
            }

            field.EnsureWireType(WireType.LengthPrefixed);
            var length = reader.ReadVarUInt32();
            var result = reader.ReadBytes(length);
            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        /// <inheritdoc/>
        void IFieldCodec<Memory<byte>>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Memory<byte> value) => WriteField(ref writer, fieldIdDelta, expectedType, value);

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型在静态分析时已知）。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Memory<byte> value) where TBufferWriter : IBufferWriter<byte>
            => WriteField(ref writer, fieldIdDelta, typeof(Memory<byte>), value);

        private static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Memory<byte> value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(Memory<byte>), WireType.LengthPrefixed);
            writer.WriteVarUInt32((uint)value.Length);
            writer.Write(value.Span);
        }
    }

    /// <summary>
    /// <see cref="Memory{T}"/>（元素类型为 <see cref="byte"/>）的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class MemoryOfByteCopier : IDeepCopier<Memory<byte>>
    {
        /// <inheritdoc/>
        Memory<byte> IDeepCopier<Memory<byte>>.DeepCopy(Memory<byte> input, CopyContext _) => DeepCopy(input, _);

        /// <summary>
        /// 复制输入。
        /// </summary>
        /// <param name="input">输入。</param>
        /// <param name="copyContext">复制上下文。</param>
        /// <returns>输入的副本。</returns>
        public static Memory<byte> DeepCopy(Memory<byte> input, CopyContext copyContext)
        {
            if (input.IsEmpty)
            {
                return default;
            }

            return input.ToArray();
        }
    }

    /// <summary>
    /// <see cref="PooledBuffer"/> 实例的序列化器。
    /// </summary>
    /// <remarks>
    /// 写入操作会消耗所提供的缓冲区并释放其存储空间，即使序列化失败也是如此。
    /// 反序列化后的缓冲区由接收方拥有，接收方在使用完毕后负责释放。
    /// </remarks>
    [RegisterSerializer]
    public sealed class PooledBufferCodec : IFieldCodec<PooledBuffer>
    {
        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] PooledBuffer value) where TBufferWriter : IBufferWriter<byte>
        {
            try
            {
                ReferenceCodec.MarkValueField(writer.Session);
                writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(PooledBuffer), WireType.LengthPrefixed);
                writer.WriteVarUInt32((uint)value.Length);
                value.CopyTo(ref writer);
            }
            finally
            {
                // 序列化会消耗此值，即使写入器在写入过程中发生故障也是如此。
                value.Reset();
            }
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public PooledBuffer ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            ReferenceCodec.MarkValueField(reader.Session);
            field.EnsureWireType(WireType.LengthPrefixed);
            const int MaxSpanLength = 4096;
            var encodedLength = reader.ReadVarUInt32();
            if (encodedLength > int.MaxValue)
            {
                ThrowInvalidSizeException(encodedLength);
            }

            reader.EnsureAvailable(encodedLength);
            var length = (int)encodedLength;
            var value = new PooledBuffer();
            while (length > 0)
            {
                var copied = Math.Min(length, MaxSpanLength);
                var span = value.GetSpan(copied)[..copied];
                reader.ReadBytes(span);
                value.Advance(copied);
                length -= copied;
            }

            Debug.Assert(length == 0);
            return value;
        }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidSizeException(uint length) => throw new IndexOutOfRangeException(
            $"The declared PooledBuffer length, {length}, exceeds the maximum supported length, {int.MaxValue}.");
    }

    /// <summary>
    /// <see cref="PooledBuffer"/> 实例的复制器，假定这些实例不可变。
    /// </summary>
    [RegisterCopier]
    public sealed class PooledBufferCopier : IDeepCopier<PooledBuffer>, IOptionalDeepCopier
    {
        /// <inheritdoc />
        public PooledBuffer DeepCopy(PooledBuffer input, CopyContext context) => input;

        /// <inheritdoc />
        public bool IsShallowCopyable() => true;
    }
}
