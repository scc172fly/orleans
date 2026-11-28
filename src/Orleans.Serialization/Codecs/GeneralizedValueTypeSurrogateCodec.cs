using Orleans.Serialization.Buffers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <typeparamref name="TField"/> 及其所有派生类型的代理序列化器。
    /// </summary>
    /// <typeparam name="TField">此类的实现所支持的类型。</typeparam>
    /// <typeparam name="TSurrogate">代替 <typeparamref name="TField"/> 进行序列化的代理类型。</typeparam>
    public abstract class GeneralizedValueTypeSurrogateCodec<TField, TSurrogate> : IFieldCodec<TField> where TField : struct where TSurrogate : struct
    {
        private readonly Type CodecFieldType = typeof(TField);
        private readonly IValueSerializer<TSurrogate> _surrogateSerializer;

        /// <summary>
        /// 初始化 <see cref="GeneralizedValueTypeSurrogateCodec{TField, TSurrogate}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        protected GeneralizedValueTypeSurrogateCodec(IValueSerializer<TSurrogate> surrogateSerializer)
        {
            _surrogateSerializer = surrogateSerializer;
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public TField ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);
            TSurrogate surrogate = default;
            _surrogateSerializer.Deserialize(ref reader, ref surrogate);
            var result = ConvertFromSurrogate(ref surrogate);
            return result;
        }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, CodecFieldType);
            TSurrogate surrogate = default;
            ConvertToSurrogate(value, ref surrogate);
            _surrogateSerializer.Serialize(ref writer, ref surrogate);
            writer.WriteEndObject();
        }

        /// <summary>
        /// 将值从代理类型转换为字段类型。
        /// </summary>
        /// <param name="surrogate">代理对象。</param>
        /// <returns>值。</returns>
        public abstract TField ConvertFromSurrogate(ref TSurrogate surrogate);

        /// <summary>
        /// 将值转换为代理类型。
        /// </summary>
        /// <param name="value">值。</param>
        /// <param name="surrogate">代理对象。</param>
        public abstract void ConvertToSurrogate(TField value, ref TSurrogate surrogate);
    }
}
