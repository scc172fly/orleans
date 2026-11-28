using Orleans.Serialization.Activators;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 可实例化引用类型的序列化器。
    /// </summary>
    /// <typeparam name="TField">字段类型。</typeparam>
    /// <typeparam name="TBaseCodec">部分序列化器的实现类型。</typeparam>
    public sealed class ConcreteTypeSerializer<TField, TBaseCodec> : IFieldCodec<TField> where TField : class where TBaseCodec : IBaseCodec<TField>
    {
        private readonly Type CodecFieldType = typeof(TField);
        private readonly IActivator<TField> _activator;
        private readonly TBaseCodec _serializer;

        /// <summary>
        /// 初始化 <see cref="ConcreteTypeSerializer{TField, TBaseCodec}"/> 类的新实例。
        /// </summary>
        /// <param name="activator">激活器。</param>
        /// <param name="serializer">序列化器。</param>
        public ConcreteTypeSerializer(IActivator<TField> activator, TBaseCodec serializer)
        {
            _activator = activator;
            _serializer = serializer;
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value is null || value.GetType() as object == CodecFieldType as object)
            {
                if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value!))
                {
                    return;
                }

                writer.WriteStartObject(fieldIdDelta, expectedType, CodecFieldType);
                _serializer.Serialize(ref writer, value!);
                writer.WriteEndObject();
            }
            else
            {
                writer.SerializeUnexpectedType(fieldIdDelta, expectedType, value);
            }
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public TField ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<TField, TInput>(ref reader, field);
            }

            var fieldType = field.FieldType;
            if (fieldType is null || fieldType == CodecFieldType)
            {
                var result = _activator.Create();
                ReferenceCodec.RecordObject(reader.Session, result);
                _serializer.Deserialize(ref reader, result);
                return result;
            }

            return reader.DeserializeUnexpectedType<TInput, TField>(ref field);
        }

        /// <summary>
        /// 读取已知运行时类型为 <typeparamref name="TField"/> 的值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">字段头。</param>
        /// <returns>反序列化得到的值。</returns>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public TField ReadValueSealed<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<TField, TInput>(ref reader, field);
            }

            var result = _activator.Create();
            ReferenceCodec.RecordObject(reader.Session, result);
            _serializer.Deserialize(ref reader, result);
            return result;
        }
    }
}
