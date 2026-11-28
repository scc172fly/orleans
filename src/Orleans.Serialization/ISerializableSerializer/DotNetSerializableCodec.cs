using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security;

namespace Orleans.Serialization
{
    /// <summary>
    /// 实现 <see cref="ISerializable"/> 模式的类型的序列化器。
    /// </summary>
    [Alias("ISerializable")]
#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode(
        "ISerializable support reflects over serialization constructors and callback methods. "
        + "Preserve public and non-public constructors and methods on serialized types.")]
#endif
    public class DotNetSerializableCodec : IGeneralizedCodec
    {
        /// <summary>
        /// 用于标识此编解码器的类型。
        /// </summary>
        public static readonly Type CodecType = typeof(DotNetSerializableCodec);
        private static readonly Type SerializableType = typeof(ISerializable);
        private readonly SerializationCallbacksFactory _serializationCallbacks;
        private readonly Func<Type, Action<object, SerializationInfo, StreamingContext>> _createConstructorDelegate;
        private readonly ConcurrentDictionary<Type, Action<object, SerializationInfo, StreamingContext>> _constructors = new();
#pragma warning disable SYSLIB0050 // Type or member is obsolete
        private readonly IFormatterConverter _formatterConverter;
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        private readonly StreamingContext _streamingContext;
        private readonly SerializationEntryCodec _entrySerializer;
        private readonly TypeConverter _typeConverter;
        private readonly ValueTypeSerializerFactory _valueTypeSerializerFactory;

        /// <summary>
        /// 初始化 <see cref="DotNetSerializableCodec"/> 类的新实例。
        /// </summary>
        /// <param name="typeResolver">类型解析器。</param>
        public DotNetSerializableCodec(TypeConverter typeResolver)
        {
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            _streamingContext = new StreamingContext(StreamingContextStates.All);
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            _typeConverter = typeResolver;
            _entrySerializer = new SerializationEntryCodec();
            _serializationCallbacks = new SerializationCallbacksFactory();
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            _formatterConverter = new FormatterConverter();
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            var constructorFactory = new SerializationConstructorFactory();
            _createConstructorDelegate = constructorFactory.GetSerializationConstructorDelegate;

            _valueTypeSerializerFactory = new ValueTypeSerializerFactory(
                _entrySerializer,
                constructorFactory,
                _serializationCallbacks,
                _formatterConverter,
                _streamingContext);
        }

        /// <inheritdoc />
        [SecurityCritical]
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "ReferenceCodec handles null serialized values and returns before the value is accessed.")]
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value!))
            {
                return;
            }

            var type = value!.GetType();
            writer.WriteFieldHeader(fieldIdDelta, expectedType, CodecType, WireType.TagDelimited);
            if (type.IsValueType)
            {
                var serializer = _valueTypeSerializerFactory.GetSerializer(type);
                serializer.WriteValue(ref writer, value);
            }
            else
            {
                WriteObject(ref writer, type, value);
            }

            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [SecurityCritical]
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public object? ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.IsReference)
            {
                return ReferenceCodec.ReadReference(ref reader, field.FieldType);
            }

            field.EnsureWireTypeTagDelimited();

            var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            Type type;
            var header = reader.ReadFieldHeader();
            if (header.FieldIdDelta == 1)
            {
                // 这是异常类型，因此按异常进行反序列化。
                var typeName = StringCodec.ReadValue(ref reader, header);
                if (!_typeConverter.TryParse(typeName!, out type))
                {
                    return ReadFallbackException(ref reader, typeName!, placeholderReferenceId);
                }
            }
            else
            {
                type = TypeSerializerCodec.ReadValue(ref reader, header)!;

                if (type.IsValueType)
                {
                    var serializer = _valueTypeSerializerFactory.GetSerializer(type);
                    return serializer.ReadValue(ref reader, type);
                }
            }

            return ReadObject(ref reader, type, placeholderReferenceId);
        }

        private object ReadFallbackException<TInput>(ref Reader<TInput> reader, string typeName, uint placeholderReferenceId)
        {
            // 将未知异常反序列化为回退类型。这意味着缺失的字段不会被表示。
            var result = (UnavailableExceptionFallbackException)ReadObject(ref reader, typeof(UnavailableExceptionFallbackException), placeholderReferenceId);
            result.ExceptionType = typeName;
            return result;
        }

        private object ReadObject<TInput>(ref Reader<TInput> reader, Type type, uint placeholderReferenceId)
        {
            var callbacks = _serializationCallbacks.GetReferenceTypeCallbacks(type);

#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var info = new SerializationInfo(type, _formatterConverter);
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            var result = RuntimeHelpers.GetUninitializedObject(type);
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            callbacks.OnDeserializing?.Invoke(result, _streamingContext);

            uint fieldId = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                if (fieldId == 1)
                {
                    var entry = _entrySerializer.ReadValue(ref reader, header);
                    if (entry.ObjectType is { } entryType)
                    {
                        info.AddValue(entry.Name!, entry.Value, entryType);
                    }
                    else
                    {
                        info.AddValue(entry.Name!, entry.Value);
                    }
                }
                else
                {
                    reader.ConsumeUnknownField(header);
                }
            }

            var constructor = _constructors.GetOrAdd(info.ObjectType, _createConstructorDelegate);
            constructor(result, info, _streamingContext);
            callbacks.OnDeserialized?.Invoke(result, _streamingContext);
            if (result is IDeserializationCallback callback)
            {
                callback.OnDeserialization(_streamingContext.Context);
            }

            return result;
        }

        private void WriteObject<TBufferWriter>(ref Writer<TBufferWriter> writer, Type type, object value) where TBufferWriter : IBufferWriter<byte>
        {
            var callbacks = _serializationCallbacks.GetReferenceTypeCallbacks(type);
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var info = new SerializationInfo(type, _formatterConverter);
#pragma warning restore SYSLIB0050 // Type or member is obsolete

            // 根据 SerializationInfo 中填充的值序列化类型名称。
            if (value is Exception)
            {
                // 对于异常，将类型序列化为字符串，以便安全地反序列化。
                var typeName = _typeConverter.Format(info.ObjectType);
                StringCodec.WriteField(ref writer, 1, typeName);
            }
            else
            {
                TypeSerializerCodec.WriteField(ref writer, 0, info.ObjectType);
            }

            callbacks.OnSerializing?.Invoke(value, _streamingContext);
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            ((ISerializable)value).GetObjectData(info, _streamingContext);
#pragma warning restore SYSLIB0050 // Type or member is obsolete

            var first = true;
            foreach (var field in info)
            {
                var surrogate = new SerializationEntrySurrogate
                {
                    Name = field.Name,
                    Value = field.Value,
                    ObjectType = field.ObjectType
                };

                _entrySerializer.WriteField(ref writer, first ? 1 : (uint)0, typeof(SerializationEntrySurrogate), surrogate);
                if (first)
                {
                    first = false;
                }
            }

            callbacks.OnSerialized?.Invoke(value, _streamingContext);
        }

        /// <inheritdoc />
        [SecurityCritical]
        public bool IsSupportedType(Type type) =>
            type == CodecType || typeof(Exception).IsAssignableFrom(type) || SerializableType.IsAssignableFrom(type) && SerializationConstructorFactory.HasSerializationConstructor(type);
    }
}
