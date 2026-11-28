using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.Net;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="IPEndPoint"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class IPEndPointCodec : IFieldCodec<IPEndPoint>, IDerivedTypeCodec
    {
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        IPEndPoint IFieldCodec<IPEndPoint>.ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取一个值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        /// <returns>值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static IPEndPoint ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field)
        {
            if (field.IsReference)
            {
                return ReferenceCodec.ReadReference<IPEndPoint, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var referencePlaceholder = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            Field header = default;
            var port = 0;

            reader.ReadFieldHeader(ref header);
            if (!header.HasFieldId || header.FieldIdDelta != 0) throw new RequiredFieldMissingException("Serialized IPEndPoint is missing its address field.");
            var address = IPAddressCodec.ReadValue(ref reader, header);

            reader.ReadFieldHeader(ref header);
            if (header.HasFieldId && header.FieldIdDelta == 1)
            {
                port = UInt16Codec.ReadValue(ref reader, header);
                reader.ReadFieldHeader(ref header);
            }

            reader.ConsumeEndBaseOrEndObject(ref header);

            var result = new IPEndPoint(address!, port);
            ReferenceCodec.RecordObject(reader.Session, result, referencePlaceholder);
            return result;
        }

        void IFieldCodec<IPEndPoint>.WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] IPEndPoint value)
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, typeof(IPEndPoint), value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(IPEndPoint), WireType.TagDelimited);
            IPAddressCodec.WriteField(ref writer, 0, value.Address);
            if (value.Port != 0) UInt16Codec.WriteField(ref writer, 1, (ushort)value.Port);
            writer.WriteEndObject();
        }

        /// <summary>
        /// 写入字段而不写入类型信息（预期类型已静态确定）。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
        /// <param name="writer">写入器。</param>
        /// <param name="fieldIdDelta">字段标识符增量。</param>
        /// <param name="value">值。</param>
        public static void WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, IPEndPoint value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceFieldExpected(ref writer, fieldIdDelta, value))
            {
                return;
            }

            System.Diagnostics.Debug.Assert(value is not null);
            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.TagDelimited);
            IPAddressCodec.WriteField(ref writer, 0, value.Address);
            if (value.Port != 0) UInt16Codec.WriteField(ref writer, 1, (ushort)value.Port);
            writer.WriteEndObject();
        }
    }

    [RegisterCopier]
    internal sealed class EndPointCopier : ShallowCopier<EndPoint>, IDerivedTypeCopier { }
}
