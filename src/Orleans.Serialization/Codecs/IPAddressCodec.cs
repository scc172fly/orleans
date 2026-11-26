using System;
using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="IPAddress"/>的序列器。
    /// </summary>
    [RegisterSerializer]
    public sealed class IPAddressCodec : IFieldCodec<IPAddress>, IDerivedTypeCodec
    {
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        IPAddress IFieldCodec<IPAddress>.ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取值 。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        /// <returns>数值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static IPAddress ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field)
        {
            if (field.IsReference)
            {
                return ReferenceCodec.ReadReference<IPAddress, TInput>(ref reader, field);
            }

            field.EnsureWireType(WireType.LengthPrefixed);
            var result = ReadRaw(ref reader);
            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        /// <summary>
        /// 读取原始长度前缀IP地址值。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IPAddress ReadRaw<TInput>(ref Buffers.Reader<TInput> reader)
        {
            var length = reader.ReadVarUInt32();
#if NET5_0_OR_GREATER
            if (reader.TryReadBytes((int)length, out var bytes))
                return new(bytes);
#endif
            return new(reader.ReadBytes(length));
        }

        void IFieldCodec<IPAddress>.WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] IPAddress value)
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, typeof(IPAddress), value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(IPAddress), WireType.LengthPrefixed);
            WriteRaw(ref writer, value);
        }

        /// <summary>
        /// 写入没有类型信息( 期待的类型为静态) 的字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldIdDelta">字段识别符号三角形。</param>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, IPAddress value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceFieldExpected(ref writer, fieldIdDelta, value))
            {
                return;
            }

            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.LengthPrefixed);
            WriteRaw(ref writer, value);
        }

        /// <summary>
        /// 写入原始长度前缀IP地址值。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteRaw<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, IPAddress value) where TBufferWriter : IBufferWriter<byte>
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);

            writer.EnsureContiguous(1 + 16);
            var span = writer.WritableSpan;
            if (!value.TryWriteBytes(span[1..], out var length)) ThrowNotSupported();
            span[0] = (byte)(length * 2 + 1); // VarInt 长度
            writer.AdvanceSpan(1 + length);
        }

        private static void ThrowNotSupported() => throw new NotSupportedException();
    }

    [RegisterCopier]
    internal sealed class IPAddressCopier : ShallowCopier<IPAddress>, IDerivedTypeCopier { }
}
