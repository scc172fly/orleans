using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 用于消耗未知字段的扩展方法。
    /// </summary>
    public static class ConsumeFieldExtension
    {
        /// <summary>
        /// 消耗未知字段。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        public static void ConsumeUnknownField<TInput>(this ref Reader<TInput> reader, Field field) => ConsumeUnknownField(ref reader, ref field);

        /// <summary>
        /// 消耗未知字段。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段。</param>
        public static void ConsumeUnknownField<TInput>(this ref Reader<TInput> reader, scoped ref Field field)
        {
            // 引用本身不能再被引用。
            if (field.WireType == WireType.Reference)
            {
                ReferenceCodec.MarkValueField(reader.Session);
                _ = reader.ReadVarUInt32();
                return;
            }

            // 记录占位符，以便稍后在该字段被引用时能够正确反序列化它。
            ReferenceCodec.RecordObject(reader.Session, new UnknownFieldMarker(field, reader.Position));

            switch (field.WireType)
            {
                case WireType.VarInt:
                    _ = reader.ReadVarUInt64();
                    break;
                case WireType.TagDelimited:
                    // 由于标记分隔字段可能由其他字段组成，因此也要递归地消耗这些字段。
                    reader.ConsumeTagDelimitedField();
                    break;
                case WireType.LengthPrefixed:
                    SkipFieldExtension.SkipLengthPrefixedField(ref reader);
                    break;
                case WireType.Fixed32:
                    reader.Skip(4);
                    break;
                case WireType.Fixed64:
                    reader.Skip(8);
                    break;
                case WireType.Extended:
                    SkipFieldExtension.ThrowUnexpectedExtendedWireType(field);
                    break;
                default:
                    SkipFieldExtension.ThrowUnexpectedWireType(field);
                    break;
            }
        }

        /// <summary>
        /// 消耗由标记分隔的字段。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        private static void ConsumeTagDelimitedField<TInput>(this ref Reader<TInput> reader)
        {
            while (true)
            {
                var field = reader.ReadFieldHeader();
                if (field.IsEndObject)
                {
                    break;
                }

                if (field.IsEndBaseFields)
                {
                    continue;
                }

                reader.ConsumeUnknownField(field);
            }
        }
    }
}
