using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="string"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class StringCodec : IFieldCodec<string>
    {
        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        string IFieldCodec<string>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static string ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<string, TInput>(ref reader, field);
            }

            field.EnsureWireType(WireType.LengthPrefixed);
            var result = ReadRaw(ref reader, reader.ReadVarUInt32());
            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        /// <summary>
        /// 读取原始字符串内容。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="numBytes">编码后的字符串长度（字节）。</param>
        /// <returns>解码后的字符串。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ReadRaw<TInput>(ref Reader<TInput> reader, uint numBytes)
        {
            reader.EnsureAvailable(numBytes);
            if (numBytes > int.MaxValue)
                ThrowInvalidSizeException(numBytes);

            if (reader.TryReadBytes((int)numBytes, out var span))
                return Encoding.UTF8.GetString(span);

            return ReadMultiSegment(ref reader, numBytes);
        }

        private static string ReadMultiSegment<TInput>(ref Reader<TInput> reader, uint numBytes)
        {
            var array = ArrayPool<byte>.Shared.Rent((int)numBytes);
            var span = array.AsSpan(0, (int)numBytes);
            reader.ReadBytes(span);
            var res = Encoding.UTF8.GetString(span);
            ArrayPool<byte>.Shared.Return(array);
            return res;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidSizeException(uint numBytes) => throw new IndexOutOfRangeException(
            $"The declared string length, {numBytes}, exceeds the maximum supported length.");

        void IFieldCodec<string>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] string value)
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
                return;

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(string), WireType.LengthPrefixed);
            var numBytes = Encoding.UTF8.GetByteCount(value);
            writer.WriteVarUInt32((uint)numBytes);
            WriteRaw(ref writer, value, numBytes);
        }

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型已静态确定）。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] string value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceFieldExpected(ref writer, fieldIdDelta, value))
            {
                return;
            }

            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.LengthPrefixed);
            var numBytes = Encoding.UTF8.GetByteCount(value);
            writer.WriteVarUInt32((uint)numBytes);
            WriteRaw(ref writer, value, numBytes);
        }

        /// <summary>
        /// 写入原始字符串内容。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="value">要编码的字符串。</param>
        /// <param name="numBytes">编码后的字符串长度（字节）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteRaw<TBufferWriter>(ref Writer<TBufferWriter> writer, string value, int numBytes) where TBufferWriter : IBufferWriter<byte>
        {
            if (numBytes < 512)
            {
                writer.EnsureContiguous(numBytes);
            }

            var currentSpan = writer.WritableSpan;

            // 如果当前跨度（Span）有足够空间容纳编码后的数据，
            // 则直接将数据编码到输出缓冲区中。
            if (numBytes <= currentSpan.Length)
            {
                writer.AdvanceSpan(Encoding.UTF8.GetBytes(value, currentSpan));
            }
            else
            {
                WriteMultiSegment(ref writer, value, numBytes);
            }
        }

        private static void WriteMultiSegment<TBufferWriter>(ref Writer<TBufferWriter> writer, string value, int remainingBytes) where TBufferWriter : IBufferWriter<byte>
        {
            var encoder = Encoding.UTF8.GetEncoder();
            var input = value.AsSpan();

            while (true)
            {
                encoder.Convert(input, writer.WritableSpan, true, out var charsUsed, out var bytesWritten, out var completed);
                writer.AdvanceSpan(bytesWritten);

                if (completed)
                {
                    Debug.Assert(charsUsed == input.Length && bytesWritten == remainingBytes);
                    break;
                }

                remainingBytes -= bytesWritten;
                input = input[charsUsed..];

                writer.Allocate(Math.Min(remainingBytes, Writer<TBufferWriter>.MaxMultiSegmentSizeHint));
            }
        }
    }
}
