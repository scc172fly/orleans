using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="DateTime"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class DateTimeCodec : IFieldCodec<DateTime>
    {
        void IFieldCodec<DateTime>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] DateTime value)
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(DateTime), WireType.Fixed64);
            writer.WriteInt64(value.ToBinary());
        }

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型已静态确定）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, DateTime value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.Fixed64);
            writer.WriteInt64(value.ToBinary());
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        DateTime IFieldCodec<DateTime>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取 <see cref="DateTime"/> 值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns><see cref="DateTime"/> 值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static DateTime ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            ReferenceCodec.MarkValueField(reader.Session);
            field.EnsureWireType(WireType.Fixed64);
            return DateTime.FromBinary(reader.ReadInt64());
        }
    }
}
