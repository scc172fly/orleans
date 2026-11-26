using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Type"/>的序列带。
    /// </summary>
    [RegisterSerializer]
    public sealed class TypeSerializerCodec : IFieldCodec<Type>, IDerivedTypeCodec
    {
        void IFieldCodec<Type>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Type value)
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, typeof(Type), value))
            {
                return;
            }

            writer.WriteStartObject(fieldIdDelta, expectedType, typeof(Type));
            WriteField(ref writer, value);
        }

        /// <summary>
        /// 写入没有类型信息( 期待的类型为静态) 的字段。
        /// </summary>
        /// <typeparam name="TBufferWriter">缓冲写作类型。</typeparam>
        /// <param name="writer">作家。</param>
        /// <param name="fieldIdDelta">字段识别符号三角形。</param>
        /// <param name="value">数值。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceFieldExpected(ref writer, fieldIdDelta, value))
            {
                return;
            }

            writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.TagDelimited);
            WriteField(ref writer, value);
        }

        private static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, Type value) where TBufferWriter : IBufferWriter<byte>
        {
            var schemaType = writer.Session.WellKnownTypes.TryGetWellKnownTypeId(value, out var id) ? SchemaType.WellKnown
                : writer.Session.ReferencedTypes.TryGetTypeReference(value, out id) ? SchemaType.Referenced
                : SchemaType.Encoded;

            // 写入编码类型 。
            ByteCodec.WriteField(ref writer, 0, (byte)schemaType);

            if (schemaType == SchemaType.Encoded)
            {
                // 如果该类型编码为编码,请写入长度前置字节。
                ReferenceCodec.MarkValueField(writer.Session);
                writer.WriteFieldHeaderExpected(1, WireType.LengthPrefixed);
                writer.Session.TypeCodec.WriteLengthPrefixed(ref writer, value);
            }
            else
            {
                // 如果该类型被引用或广为人知,请以 varint 形式写入。
                UInt32Codec.WriteField(ref writer, 2, id);
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        Type IFieldCodec<Type>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        /// <summary>
        /// 读取值 。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        /// <returns>数值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public static Type ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Type, TInput>(ref reader, field);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            uint fieldId = 0;
            var schemaType = default(SchemaType);
            uint id = 0;
            Type? result = null;
            while (true)
            {
                reader.ReadFieldHeader(ref field);
                if (field.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += field.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        schemaType = (SchemaType)ByteCodec.ReadValue(ref reader, field);
                        break;
                    case 1:
                        ReferenceCodec.MarkValueField(reader.Session);
                        result = reader.Session.TypeCodec.ReadLengthPrefixed(ref reader);
                        break;
                    case 2:
                        id = UInt32Codec.ReadValue(ref reader, field);
                        break;
                    default:
                        reader.ConsumeUnknownField(field);
                        break;
                }
            }

            switch (schemaType)
            {
                case SchemaType.Referenced:
                    result = reader.Session.ReferencedTypes.GetReferencedType(id);
                    break;

                case SchemaType.WellKnown:
                    if (!reader.Session.WellKnownTypes.TryGetWellKnownType(id, out result))
                        ThrowUnknownWellKnownType(id);
                    break;

                case SchemaType.Encoded:
                    // 类型代码器不应更新类型参考地图, 否则未知字段的脱机会损坏
                    break;

                default:
                    ThrowInvalidSchemaType(schemaType);
                    break;
            }

            if (result is null) ThrowMissingType();
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            return result;
        }

        [DoesNotReturn]
        private static void ThrowInvalidSchemaType(SchemaType schemaType) => throw new NotSupportedException(
            $"SchemaType {schemaType} is not supported by {nameof(TypeSerializerCodec)}.");

        [DoesNotReturn]
        private static void ThrowUnknownWellKnownType(uint id) => throw new UnknownWellKnownTypeException(id);

        [DoesNotReturn]
        private static void ThrowMissingType() => throw new TypeMissingException();
    }
}
