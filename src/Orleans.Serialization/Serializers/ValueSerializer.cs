using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 值类型的序列化器。
    /// </summary>
    /// <typeparam name="TField">字段类型。</typeparam>
    /// <typeparam name="TValueSerializer">值类型序列化器的实现类型。</typeparam>
    public sealed class ValueSerializer<TField, TValueSerializer> : IFieldCodec<TField> where TField : struct where TValueSerializer : IValueSerializer<TField>
    {
        private readonly Type CodecFieldType = typeof(TField);
        private readonly TValueSerializer _serializer;

        /// <summary>
        /// 初始化 <see cref="ValueSerializer{TField, TValueSerializer}"/> 类的新实例。
        /// </summary>
        /// <param name="serializer">序列化器。</param>
        public ValueSerializer(TValueSerializer serializer)
        {
            _serializer = serializer;
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, CodecFieldType);
            _serializer.Serialize(ref writer, ref value);
            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public TField ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            ReferenceCodec.MarkValueField(reader.Session);
            var value = default(TField);
            _serializer.Deserialize(ref reader, ref value);
            return value;
        }
    }
}
