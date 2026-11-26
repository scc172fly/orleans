using System;
using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <typeparamref name="TField"/>的代用序列器。
    /// </summary>
    /// <typeparam name="TField">执行此类支持的类型。</typeparam>
    /// <typeparam name="TSurrogate">代替代用类型 <typeparamref name="TField"/>.</typeparam>
    public abstract class ReferenceTypeSurrogateCodec<TField, TSurrogate> : IFieldCodec<TField> where TSurrogate : struct
    {
        private readonly Type CodecFieldType = typeof(TField);
        private readonly IValueSerializer<TSurrogate> _surrogateSerializer;

        /// <summary>
        /// 初始化 <see cref="ReferenceTypeSurrogateCodec{TField, TSurrogate}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        protected ReferenceTypeSurrogateCodec(IValueSerializer<TSurrogate> surrogateSerializer)
        {
            _surrogateSerializer = surrogateSerializer;
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
                field.EnsureWireTypeTagDelimited();
                var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
                TSurrogate surrogate = default;
                _surrogateSerializer.Deserialize(ref reader, ref surrogate);
                var result = ConvertFromSurrogate(ref surrogate);
                ReferenceCodec.RecordObject(reader.Session, result!, placeholderReferenceId);
                return result;
            }

            // 类型是后裔,不是完全吻合 所以请具体连发器
            var specificSerializer = reader.Session.CodecProvider.GetCodec(fieldType);
            return (TField)specificSerializer.ReadValue(ref reader, field)!;
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value!))
            {
                return;
            }

            var fieldType = value!.GetType();
            if (fieldType == CodecFieldType)
            {
                writer.WriteStartObject(fieldIdDelta, expectedType, fieldType);
                TSurrogate surrogate = default;
                ConvertToSurrogate(value, ref surrogate);
                _surrogateSerializer.Serialize(ref writer, ref surrogate);
                writer.WriteEndObject();
            }
            else
            {
                writer.SerializeUnexpectedType(fieldIdDelta, expectedType, value);
            }
        }

        /// <summary>
        /// 将代数转换为字段类型。
        /// </summary>
        /// <param name="surrogate">代孕人。</param>
        /// <returns>数值。</returns>
        public abstract TField ConvertFromSurrogate(ref TSurrogate surrogate);

        /// <summary>
        /// 将一个值转换为代孕类型。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <param name="surrogate">代孕人。</param>
        public abstract void ConvertToSurrogate(TField value, ref TSurrogate surrogate);
    }
}
