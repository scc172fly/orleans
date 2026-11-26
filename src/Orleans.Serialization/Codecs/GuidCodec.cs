using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Guid"/>的序列器。
    /// </summary>
    [RegisterSerializer]
    public sealed class GuidCodec : IFieldCodec<Guid>
    {
        private const int Width = 16;

        void IFieldCodec<Guid>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Guid value)
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(Guid), WireType.LengthPrefixed);
            writer.WriteVarUInt7(Width);
            WriteRaw(ref writer, value);
        }

        /// <summary>
        /// 写入没有类型信息( 期待的类型为静态) 的字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldIdDelta">字段识别符号三角形。</param>
        /// <param name="value">数值。</param>
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Guid value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.LengthPrefixed);
            writer.WriteVarUInt7(Width);
            WriteRaw(ref writer, value);
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        Guid IFieldCodec<Guid>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取值 。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        /// <returns>数值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static Guid ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            ReferenceCodec.MarkValueField(reader.Session);

            field.EnsureWireType(WireType.LengthPrefixed);

            uint length = reader.ReadVarUInt32();
            if (length != Width)
            {
                throw new UnexpectedLengthPrefixValueException(nameof(Guid), Width, length);
            }

            return ReadRaw(ref reader);
        }

        /// <summary>
        /// 写入原始的 GUID 内容。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteRaw<TBufferWriter>(ref Writer<TBufferWriter> writer, Guid value) where TBufferWriter : IBufferWriter<byte>
        {
            if (BitConverter.IsLittleEndian)
            {
#if NET7_0_OR_GREATER
                writer.Write(MemoryMarshal.AsBytes(new Span<Guid>(ref value)));
#else
                writer.EnsureContiguous(Width);
                if (value.TryWriteBytes(writer.WritableSpan))
                {
                    writer.AdvanceSpan(Width);
                    return;
                }
                writer.Write(value.ToByteArray());
#endif
            }
            else
            {
                writer.EnsureContiguous(Width);
                var done = value.TryWriteBytes(writer.WritableSpan);
                Debug.Assert(done);
                writer.AdvanceSpan(Width);
            }
        }

        /// <summary>
        /// 读取原始的 GUID 内容。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Guid ReadRaw<TInput>(ref Reader<TInput> reader)
        {
#if NET7_0_OR_GREATER
            Unsafe.SkipInit(out Guid res);
            var bytes = MemoryMarshal.AsBytes(new Span<Guid>(ref res));
            reader.ReadBytes(bytes);

            if (BitConverter.IsLittleEndian)
                return res;

            return new Guid(bytes);
#else
            if (reader.TryReadBytes(Width, out var readOnly))
            {
                return new Guid(readOnly);
            }

            Span<byte> bytes = stackalloc byte[Width];
            reader.ReadBytes(bytes);

            return new Guid(bytes);
#endif
        }
    }
}
