using System;
using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 重现器,它跳过它遇到的所有字段。
    /// </summary>
    public class SkipFieldCodec : IFieldCodec
    {
        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public object? ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            reader.SkipField(field);
            return null;
        }
    }

    /// <summary>
    /// <see cref="Reader{TInput}"/> 跳过字段的扩展方法。
    /// </summary>
    public static class SkipFieldExtension
    {
        /// <summary>
        /// 跳过当前字段。
        /// </summary>
        /// <typeparam name="TInput">读者输入类型。</typeparam>
        /// <param name="reader">读者</param>
        /// <param name="field">字段。</param>
        public static void SkipField<TInput>(this ref Reader<TInput> reader, Field field)
        {
            switch (field.WireType)
            {
                case WireType.Reference:
                case WireType.VarInt:
                    reader.Session.ReferencedObjects.MarkValueField();
                    _ = reader.ReadVarUInt64();
                    break;
                case WireType.TagDelimited:
                    reader.Session.ReferencedObjects.MarkValueField();
                    SkipTagDelimitedField(ref reader);
                    break;
                case WireType.LengthPrefixed:
                    reader.Session.ReferencedObjects.MarkValueField();
                    SkipLengthPrefixedField(ref reader);
                    break;
                case WireType.Fixed32:
                    reader.Session.ReferencedObjects.MarkValueField();
                    reader.Skip(4);
                    break;
                case WireType.Fixed64:
                    reader.Session.ReferencedObjects.MarkValueField();
                    reader.Skip(8);
                    break;
                case WireType.Extended:
                    if (!field.IsEndBaseOrEndObject)
                    {
                        ThrowUnexpectedExtendedWireType(field);
                    }

                    break;
                default:
                    ThrowUnexpectedWireType(field);
                    break;
            }
        }

        internal static void ThrowUnexpectedExtendedWireType(Field field) => throw new ArgumentOutOfRangeException(
                $"Unexpected {nameof(ExtendedWireType)} value [{field.ExtendedWireType}] in field {field} while skipping field.");

        internal static void ThrowUnexpectedWireType(Field field) => throw new ArgumentOutOfRangeException(
                $"Unexpected {nameof(WireType)} value [{field.WireType}] in field {field} while skipping field.");

        internal static void SkipLengthPrefixedField<TInput>(ref Reader<TInput> reader)
        {
            var length = reader.ReadVarUInt32();
            reader.Skip(length);
        }

        private static void SkipTagDelimitedField<TInput>(ref Reader<TInput> reader)
        {
            while (true)
            {
                var field = reader.ReadFieldHeader();
                if (field.IsEndObject)
                {
                    break;
                }

                reader.SkipField(field);
            }
        }
    }
}
