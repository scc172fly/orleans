using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 消费未知字段的扩展方法。
    /// </summary>
    public static class ConsumeFieldExtension
    {
        /// <summary>
        /// 消耗了一个未知的字段。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        public static void ConsumeUnknownField<TInput>(this ref Reader<TInput> reader, Field field) => ConsumeUnknownField(ref reader, ref field);

        /// <summary>
        /// 消耗了一个未知的字段。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        public static void ConsumeUnknownField<TInput>(this ref Reader<TInput> reader, scoped ref Field field)
        {
            // 无法引用参考文献本身。
            if (field.WireType == WireType.Reference)
            {
                ReferenceCodec.MarkValueField(reader.Session);
                _ = reader.ReadVarUInt32();
                return;
            }

            // 记录占位符,以便本字段在被引用时稍后可以正确取消其音量。
            ReferenceCodec.RecordObject(reader.Session, new UnknownFieldMarker(field, reader.Position));

            switch (field.WireType)
            {
                case WireType.VarInt:
                    _ = reader.ReadVarUInt64();
                    break;
                case WireType.TagDelimited:
                    // 由于标记的划定字段可以由其它字段组成,因此这些字段也相继消耗。
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
        /// 占用了限定标签的字段。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
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
