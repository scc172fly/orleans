using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="TimeSpan"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class TimeSpanCodec : IFieldCodec<TimeSpan>
    {
        void IFieldCodec<TimeSpan>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TimeSpan value)
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(TimeSpan), WireType.Fixed64);
            writer.WriteInt64(value.Ticks);
        }

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型已静态确定）。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, TimeSpan value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.Fixed64);
            writer.WriteInt64(value.Ticks);
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        TimeSpan IFieldCodec<TimeSpan>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static TimeSpan ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            ReferenceCodec.MarkValueField(reader.Session);
            field.EnsureWireType(WireType.Fixed64);
            return TimeSpan.FromTicks(reader.ReadInt64());
        }
    }
}
