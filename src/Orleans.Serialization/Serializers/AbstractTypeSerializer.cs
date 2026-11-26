using System;
using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 用于抽象类型,因此无法即时自我转换的序列器,如抽象类别和界面类型。
    /// </summary>
    /// <typeparam name="TField">字段类型。</typeparam>
    public class AbstractTypeSerializer<TField> : AbstractTypeSerializer, IFieldCodec<TField>, IBaseCodec<TField> where TField : class
    {
        /// <summary>
        /// 初始化 <see cref="AbstractTypeSerializer{TField}"/> 类的新实例。
        /// </summary>
        protected AbstractTypeSerializer() : base(typeof(TField)) { }

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
            => base.WriteField(ref writer, fieldIdDelta, expectedType, value);

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public new TField ReadValue<TInput>(ref Reader<TInput> reader, Field field) => (TField)base.ReadValue(ref reader, field)!;

        /// <inheritdoc/>
        public virtual void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, TField instance) where TBufferWriter : IBufferWriter<byte> { }

        /// <inheritdoc/>
        public virtual void Deserialize<TReaderInput>(ref Reader<TReaderInput> reader, TField instance) => reader.ConsumeEndBaseOrEndObject();
    }

    // 类别类型限制
    internal sealed class AbstractTypeSerializerWrapper<TField> : AbstractTypeSerializer, IFieldCodec<TField>
    {
        public AbstractTypeSerializerWrapper() : base(typeof(TField)) { }

        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
            => base.WriteField(ref writer, fieldIdDelta, expectedType, value);

        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public new TField ReadValue<TInput>(ref Reader<TInput> reader, Field field) => (TField)base.ReadValue(ref reader, field)!;
    }

    /// <summary>
    /// 将申报类型无法直接即时的数值,如抽象类别和界面等分层排列为序列。
    /// </summary>
    public class AbstractTypeSerializer : IFieldCodec
    {
        private readonly Type _fieldType;

        /// <summary>
        /// 初始化 <see cref="AbstractTypeSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="fieldType">申报字段类型。</param>
        protected internal AbstractTypeSerializer(Type fieldType) => _fieldType = fieldType;

        /// <inheritdoc/>
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            var specificSerializer = writer.Session.CodecProvider.GetCodec(value.GetType());
            specificSerializer.WriteField(ref writer, fieldIdDelta, expectedType, value);
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public object? ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.IsReference)
                return ReferenceCodec.ReadReference(ref reader, field.FieldType ?? _fieldType);

            var fieldType = field.FieldType;
            if (fieldType is null)
                ThrowMissingFieldType();

            var specificSerializer = reader.Session.CodecProvider.GetCodec(fieldType!);
            return specificSerializer.ReadValue(ref reader, field);
        }

        private void ThrowMissingFieldType() => throw new FieldTypeMissingException(_fieldType);
    }
}
