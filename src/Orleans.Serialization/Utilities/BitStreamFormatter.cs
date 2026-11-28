using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace Orleans.Serialization.Utilities
{
    /// <summary>
    /// 用于将编码后的位流格式化为文本的实用工具。
    /// </summary>
    public static class BitStreamFormatter
    {
        /// <summary>
        /// 格式化给定缓冲区。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format<TInput>(ref Reader<TInput> reader)
        {
            var res = new StringBuilder();
            Format(ref reader, res);
            return res.ToString();
        }

        /// <summary>
        /// 格式化指定数组。
        /// </summary>
        /// <param name="slice">数组。</param>
        /// <param name="session">会话。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format(PooledBuffer.BufferSlice slice, SerializerSession session)
        {
            var reader = Reader.Create(slice, session);
            return Format(ref reader);
        }

        /// <summary>
        /// 格式化指定数组。
        /// </summary>
        /// <param name="array">数组。</param>
        /// <param name="session">会话。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format(byte[] array, SerializerSession session)
        {
            var reader = Reader.Create(array, session);
            return Format(ref reader);
        }

        /// <summary>
        /// 格式化指定缓冲区。
        /// </summary>
        /// <param name="input">输入缓冲区。</param>
        /// <param name="session">会话。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format(ReadOnlySpan<byte> input, SerializerSession session)
        {
            var reader = Reader.Create(input, session);
            return Format(ref reader);
        }

        /// <summary>
        /// 格式化指定缓冲区。
        /// </summary>
        /// <param name="input">输入缓冲区。</param>
        /// <param name="session">会话。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format(ReadOnlyMemory<byte> input, SerializerSession session)
        {
            var reader = Reader.Create(input, session);
            return Format(ref reader);
        }

        /// <summary>
        /// 格式化指定缓冲区。
        /// </summary>
        /// <param name="input">输入缓冲区。</param>
        /// <param name="session">会话。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format(ReadOnlySequence<byte> input, SerializerSession session)
        {
            var reader = Reader.Create(input, session);
            return Format(ref reader);
        }

        /// <summary>
        /// 格式化指定缓冲区。（译注：此重载的输入实际为 Stream。）
        /// </summary>
        /// <param name="input">输入流。</param>
        /// <param name="session">会话。</param>
        /// <returns>已格式化的输入。</returns>
        public static string Format(Stream input, SerializerSession session)
        {
            var reader = Reader.Create(input, session);
            return Format(ref reader);
        }

        /// <summary>
        /// 格式化指定缓冲区。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="result">目标字符串构建器。</param>
        public static void Format<TInput>(ref Reader<TInput> reader, StringBuilder result)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(result);
            var (field, type) = reader.ReadFieldHeaderForAnalysis();
            FormatField(ref reader, field, type, field.FieldIdDelta, result, indentation: 0);
        }

        private static void FormatField<TInput>(ref Reader<TInput> reader, Field field, string typeName, uint id, StringBuilder res, int indentation)
        {
            var indentString = new string(' ', indentation);
            AppendAddress(ref reader, res);
            res.Append(indentString);

            // 引用本身不能再被引用。
            if (field.WireType == WireType.Reference)
            {
                ReferenceCodec.MarkValueField(reader.Session);
                var refId = reader.ReadVarUInt32();
                res.Append('[');
                FormatFieldHeader(res, reader.Session, field, id, typeName);
                if (refId == 0)
                {
                    res.Append($" Reference: 0");
                }
                else
                {
                    var refd = reader.Session.ReferencedObjects.TryGetReferencedObject(refId);
                    res.Append($" Reference: {refId} ({refd}{(refd is null ? "unknown" : null)})");
                }
                res.Append(']');
                return;
            }

            // 记录占位符，以便稍后在该字段被引用时能够正确反序列化它。
            ReferenceCodec.RecordObject(reader.Session, new UnknownFieldMarker(field, reader.Position));
            res.Append('[');
            FormatFieldHeader(res, reader.Session, field, id, typeName);
            res.Append(']');
            res.Append(" Value: ");

            switch (field.WireType)
            {
                case WireType.VarInt:
                    {
                        var a = reader.ReadVarUInt64();
                        if (a < 10240)
                        {
                            res.Append($"{a} (0x{a:X})");
                        }
                        else
                        {
                            res.Append($"0x{a:X}");
                        }
                    }
                    break;
                case WireType.TagDelimited:
                    // 由于标记分隔字段可能由其他字段组成，因此也要递归地消耗这些字段。

                    res.Append($"{{\n");
                    reader.FormatTagDelimitedField(res, indentation + 1);
                    res.AppendLine();
                    AppendAddress(ref reader, res);
                    res.Append($"{indentString}}}");
                    break;
                case WireType.LengthPrefixed:
                    {
                        var length = reader.ReadVarUInt32();
                        res.Append($"(length: {length}b) [");
                        var a = reader.ReadBytes(length);
                        FormatByteArray(res, a);
                        res.Append(']');
                    }
                    break;
                case WireType.Fixed32:
                    {
                        var a = reader.ReadUInt32();
                        if (a < 10240)
                        {
                            res.Append($"{a} (0x{a:X})");
                        }
                        else
                        {
                            res.Append($"0x{a:X}");
                        }
                    }
                    break;
                case WireType.Fixed64:
                    {
                        var a = reader.ReadUInt64();
                        if (a < 10240)
                        {
                            res.Append($"{a} (0x{a:X})");
                        }
                        else
                        {
                            res.Append($"0x{a:X}");
                        }
                    }
                    break;
                case WireType.Extended:
                    SkipFieldExtension.ThrowUnexpectedExtendedWireType(field);
                    break;
                default:
                    SkipFieldExtension.ThrowUnexpectedWireType(field);
                    break;
            }
        }

        private static void FormatByteArray(StringBuilder res, byte[] a)
        {
            var isAscii = true;
            foreach (var b in a)
            {
                if (b >= 0x7F)
                {
                    isAscii = false;
                }
            }

            if (isAscii)
            {
                res.Append('"');
                res.Append(Encoding.ASCII.GetString(a));
                res.Append('"');
            }
            else
            {
                bool first = true;
                foreach (var b in a)
                {
                    if (!first)
                    {
                        res.Append(' ');
                    }
                    else
                    {
                        first = false;
                    }

                    res.Append($"{b:X2}");
                }
            }
        }

        private static void FormatFieldHeader(StringBuilder res, SerializerSession session, Field field, uint id, string typeName)
        {
            _ = res
                .Append($"#{session.ReferencedObjects.CurrentReferenceId} ")
                .Append((string)field.WireType.ToString());
            if (field.HasFieldId)
            {
                _ = res.Append($" Id: {id}");
            }

            if (field.IsSchemaTypeValid)
            {
                _ = res.Append($" SchemaType: {field.SchemaType}");
            }

            if (field.HasExtendedSchemaType)
            {
                _ = res.Append($" RuntimeType: {field.FieldType} (name: {typeName})");
            }

            if (field.WireType == WireType.Extended)
            {
                _ = res.Append($": {field.ExtendedWireType}");
            }
        }

        /// <summary>
        /// 消耗由标记分隔的字段。
        /// </summary>
        private static void FormatTagDelimitedField<TInput>(this ref Reader<TInput> reader, StringBuilder res, int indentation)
        {
            var id = 0U;
            var first = true;
            while (true)
            {
                var (field, type) = reader.ReadFieldHeaderForAnalysis();
                if (field.IsEndObject)
                {
                    break;
                }

                if (field.IsEndBaseFields)
                {
                    res.AppendLine();
                    AppendAddress(ref reader, res);
                    res.Append($"{new string(' ', indentation)}- End of base type fields -");
                    if (first)
                    {
                        first = false;
                    }

                    id = 0U;
                    continue;
                }

                id += field.FieldIdDelta;
                if (!first)
                {
                    res.AppendLine();
                }
                else
                {
                    first = false;
                }

                FormatField(ref reader, field, type, id, res, indentation);
            }
        }

        private static void AppendAddress<TInput>(ref Reader<TInput> reader, StringBuilder res)
        {
            res.Append($"0x{reader.Position:X4} ");
        }
    }
}
