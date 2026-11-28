using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization
{
    /// <summary>
    /// <see cref="Exception"/> 类型的序列化器。
    /// </summary>
    [RegisterSerializer]
    [RegisterCopier]
    [Alias("Exception")]
    public sealed class ExceptionCodec : IFieldCodec<Exception>, IBaseCodec<Exception>, IGeneralizedCodec, IGeneralizedBaseCodec, IBaseCopier<Exception>
    {
        private readonly StreamingContext _streamingContext;
#pragma warning disable SYSLIB0050 // Type or member is obsolete
        private readonly FormatterConverter _formatterConverter;
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        private readonly Action<object, SerializationInfo, StreamingContext> _baseExceptionConstructor;
        private readonly TypeConverter _typeConverter;
        private readonly IFieldCodec<Dictionary<object, object?>> _dictionaryCodec;
        private readonly IDeepCopier<Dictionary<object, object?>> _dictionaryCopier;
        private readonly IDeepCopier<Exception> _exceptionCopier;
        private readonly ExceptionSerializationOptions _options;

        /// <summary>
        /// 初始化 <see cref="ExceptionCodec"/> 类的新实例。
        /// </summary>
        /// <param name="typeConverter">类型转换器。</param>
        /// <param name="dictionaryCodec">字典编解码器。</param>
        /// <param name="dictionaryCopier">字典复制器。</param>
        /// <param name="exceptionCopier">异常复制器。</param>
        /// <param name="exceptionSerializationOptions">异常序列化选项。</param>
        public ExceptionCodec(
            TypeConverter typeConverter,
            IFieldCodec<Dictionary<object, object?>> dictionaryCodec,
            IDeepCopier<Dictionary<object, object?>> dictionaryCopier,
            IDeepCopier<Exception> exceptionCopier,
            IOptions<ExceptionSerializationOptions> exceptionSerializationOptions)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(exceptionSerializationOptions);

#pragma warning disable SYSLIB0050 // Type or member is obsolete
            _streamingContext = new StreamingContext(StreamingContextStates.All);
            _formatterConverter = new FormatterConverter();
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            _baseExceptionConstructor = new SerializationConstructorFactory().GetSerializationConstructorDelegate(typeof(Exception));
            _typeConverter = typeConverter;
            _dictionaryCodec = dictionaryCodec;
            _dictionaryCopier = dictionaryCopier;
            _exceptionCopier = exceptionCopier;
            _options = exceptionSerializationOptions.Value;
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The serializer dispatches this base codec with an allocated non-null exception instance.")]
        public void Deserialize<TInput>(ref Reader<TInput> reader, Exception value)
        {
            uint fieldId = 0;
            string? message = null;
            string? stackTrace = null;
            Exception? innerException = null;
            Dictionary<object, object?>? data = null;
            int hResult = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        message = StringCodec.ReadValue(ref reader, header);
                        break;
                    case 1:
                        stackTrace = StringCodec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        innerException = ReadValue(ref reader, header);
                        break;
                    case 3:
                        hResult = Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        data = _dictionaryCodec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            SetBasePropertiesCore(value, message, stackTrace, innerException, hResult, data);
        }

        /// <summary>
        /// 获取给定异常的对象数据。
        /// </summary>
        /// <param name="value">值。</param>
        /// <returns>已填充的 <see cref="SerializationInfo"/> 值。</returns>
        public SerializationInfo GetObjectData(Exception value)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);
            return GetObjectDataCore(value);
        }

        internal SerializationInfo GetObjectDataCore(Exception value)
        {
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var info = new SerializationInfo(value.GetType(), _formatterConverter);
#pragma warning disable SYSLIB0051 // Type or member is obsolete
            value.GetObjectData(info, _streamingContext);
#pragma warning restore SYSLIB0051 // Type or member is obsolete
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            return info;
        }

        /// <summary>
        /// 设置给定异常的基类属性。
        /// </summary>
        /// <param name="value">值。</param>
        /// <param name="message">消息。</param>
        /// <param name="stackTrace">堆栈跟踪。</param>
        /// <param name="innerException">内部异常。</param>
        /// <param name="hResult">HResult。</param>
        /// <param name="data">数据。</param>
        public void SetBaseProperties(Exception value, string? message, string? stackTrace, Exception? innerException, int hResult, Dictionary<object, object?>? data)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(value);
            SetBasePropertiesCore(value, message, stackTrace, innerException, hResult, data);
        }

        internal void SetBasePropertiesCore(Exception value, string? message, string? stackTrace, Exception? innerException, int hResult, Dictionary<object, object?>? data)
        {
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var info = new SerializationInfo(typeof(Exception), _formatterConverter);
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            info.AddValue("Message", message, typeof(string));
            info.AddValue("StackTraceString", null, typeof(string));
            info.AddValue("InnerException", innerException, typeof(Exception));
            info.AddValue("ClassName", value.GetType().ToString(), typeof(string));
            info.AddValue("Data", null, typeof(IDictionary));
            info.AddValue("HelpURL", null, typeof(string));
#if NET6_0_OR_GREATER
            info.AddValue("RemoteStackTraceString", null, typeof(string));
#else
            info.AddValue("RemoteStackTraceString", stackTrace, typeof(string));
#endif
            info.AddValue("RemoteStackIndex", 0, typeof(int));
            info.AddValue("ExceptionMethod", null, typeof(string));
            info.AddValue("HResult", hResult);
            info.AddValue("Source", null, typeof(string));
            info.AddValue("WatsonBuckets", null, typeof(byte[]));

            _baseExceptionConstructor(value, info, _streamingContext);
            if (data is { })
            {
                foreach (var pair in data)
                {
                    value.Data[pair.Key] = pair.Value;
                }
            }

#if NET6_0_OR_GREATER
            if (stackTrace is not null)
            {
                ExceptionDispatchInfo.SetRemoteStackTrace(value, stackTrace);
            }
#endif
        }

        /// <summary>
        /// 获取给定异常的 Data 属性。
        /// </summary>
        /// <param name="exception">异常。</param>
        /// <returns>给定异常的 <see cref="Exception.Data"/> 属性。</returns>
        public Dictionary<object, object?>? GetDataProperty(Exception exception)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(exception);
            return GetDataPropertyCore(exception);
        }

        internal static Dictionary<object, object?>? GetDataPropertyCore(Exception exception)
        {
            if (exception.Data is null or { Count: 0 })
            {
                return null;
            }

            var tmp = new Dictionary<object, object?>(exception.Data.Count);
            var enumerator = exception.Data.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var entry = enumerator.Entry;
                tmp[entry.Key] = entry.Value;
            }

            return tmp;
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The serializer dispatches this base codec only after handling null references.")]
        public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, Exception value) where TBufferWriter : IBufferWriter<byte>
        {
            StringCodec.WriteField(ref writer, 0, value.Message);
            StringCodec.WriteField(ref writer, 1, value.StackTrace);
            WriteField(ref writer, 1, typeof(Exception), value.InnerException!);
            Int32Codec.WriteField(ref writer, 1, value.HResult);
            if (GetDataPropertyCore(value) is { } dataDictionary)
            {
                _dictionaryCodec.WriteField(ref writer, 1, typeof(Dictionary<object, object?>), dataDictionary);
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Exception fields invoke this method only after handling null references.")]
        public void SerializeException<TBufferWriter>(ref Writer<TBufferWriter> writer, Exception value) where TBufferWriter : IBufferWriter<byte>
        {
            StringCodec.WriteField(ref writer, 0, _typeConverter.Format(value.GetType()));
            StringCodec.WriteField(ref writer, 1, value.Message);
            StringCodec.WriteField(ref writer, 1, value.StackTrace);
            WriteField(ref writer, 1, typeof(Exception), value.InnerException!);
            Int32Codec.WriteField(ref writer, 1, value.HResult);
            if (GetDataPropertyCore(value) is { } dataDictionary)
            {
                _dictionaryCodec.WriteField(ref writer, 1, typeof(Dictionary<object, object?>), dataDictionary);
            }
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Exception value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            if (value.GetType() == typeof(Exception))
            {
                // 异常绝不会作为引用写入。这可防止异常中出现引用环，是一项安全防护措施。
                ReferenceCodec.MarkValueField(writer.Session);
                writer.WriteStartObject(fieldIdDelta, expectedType, typeof(ExceptionCodec));
                SerializeException(ref writer, value);
                writer.WriteEndObject();
            }
            else
            {
                writer.SerializeUnexpectedType(fieldIdDelta, expectedType, value);
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Codec selection supplies the non-null candidate type.")]
        public bool IsSupportedType(Type type)
        {
            if (type == typeof(ExceptionCodec))
            {
                return true;
            }

            if (type == typeof(AggregateException))
            {
                return false;
            }

            if (typeof(Exception).IsAssignableFrom(type) && type.Namespace is { } ns)
            {
                if (_options.SupportedExceptionTypeFilter is { } filter && filter(type))
                {
                    return true;
                }

                foreach (var prefix in _options.SupportedNamespacePrefixes)
                {
                    if (ns.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] object? value) where TBufferWriter : IBufferWriter<byte>
        {
            if (value is null)
            {
                ReferenceCodec.WriteNullReference(ref writer, fieldIdDelta);
                return;
            }

            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteStartObject(fieldIdDelta, expectedType, typeof(ExceptionCodec));
            SerializeException(ref writer, (Exception)value);
            writer.WriteEndObject();
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public Exception ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            // 用于处理 null 值。
            if (field.WireType == WireType.Reference)
            {
                var referencedException = ReferenceCodec.ReadReference<Exception, TInput>(ref reader, field);

                // 不允许异常参与引用环，因为 .NET 不允许涉及 InnerException 的循环引用。
                // 异常绝不能通过其已知的属性或字段（例如 InnerException）形成循环对象图。
                if (referencedException is not null)
                {
                    throw new ReferenceFieldNotSupportedException(field.FieldType);
                }

                return null;
            }

            Type? valueType = field.FieldType;
            if (valueType is null || valueType == typeof(Exception))
            {
                return DeserializeException(ref reader, field);
            }

            return reader.DeserializeUnexpectedType<TInput, Exception>(ref field);
        }

        /// <inheritdoc />
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        object? IFieldCodec.ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return ReferenceCodec.ReadReference<Exception, TInput>(ref reader, field);
            }

            return DeserializeException(ref reader, field);
        }

        /// <summary>
        /// 反序列化异常值。
        /// </summary>
        /// <typeparam name="TInput">读取器的输入类型。</typeparam>
        /// <param name="reader">读取器。</param>
        /// <param name="field">异常值的字段头。</param>
        /// <returns>反序列化后的异常。</returns>
        public Exception DeserializeException<TInput>(ref Reader<TInput> reader, Field field)
        {
            field.EnsureWireTypeTagDelimited();
            ReferenceCodec.MarkValueField(reader.Session);

            uint fieldId = 0;
            string? typeName = null;
            string? message = null;
            string? stackTrace = null;
            Exception? innerException = null;
            Dictionary<object, object?>? data = null;
            int hResult = 0;
            while (true)
            {
                var header = reader.ReadFieldHeader();
                if (header.IsEndBaseOrEndObject)
                {
                    break;
                }

                fieldId += header.FieldIdDelta;
                switch (fieldId)
                {
                    case 0:
                        typeName = StringCodec.ReadValue(ref reader, header);
                        break;
                    case 1:
                        message = StringCodec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        stackTrace = StringCodec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        innerException = ReadValue(ref reader, header);
                        break;
                    case 4:
                        hResult = Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 5:
                        data = _dictionaryCodec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }
            }

            Exception result;
            if (!_typeConverter.TryParse(typeName!, out var type))
            {
                result = new UnavailableExceptionFallbackException
                {
                    ExceptionType = typeName
                };
            }
            else if (typeof(Exception).IsAssignableFrom(type))
            {
                try
                {
                    if (type.GetConstructor(Array.Empty<Type>()) is not null)
                    {
                        result = (Exception)Activator.CreateInstance(type)!;
                    }
                    else
                    {
                        result = (Exception)RuntimeHelpers.GetUninitializedObject(type);
                    }
                }
                catch (Exception constructorException)
                {
                    result = new UnavailableExceptionFallbackException($"Failed to construct exception of type \"{type}\"", constructorException)
                    {
                        ExceptionType = typeName
                    };
                }
            }
            else
            {
                throw new NotSupportedException($"Type {type} is not supported");
            }

            SetBasePropertiesCore(result, message, stackTrace, innerException, hResult, data);
            return result;
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The Orleans deep-copy pipeline supplies non-null source, destination, and context instances.")]
        public void DeepCopy(Exception input, Exception output, CopyContext context)
        {
            var info = GetObjectDataCore(input);
            SetBasePropertiesCore(
                output,
                // 从对象数据获取消息，以应对属性被重写的情况，例如 AggregateException。
                info.GetString("Message"),
                input.StackTrace,
                _exceptionCopier.DeepCopy(input.InnerException!, context),
                input.HResult,
                _dictionaryCopier.DeepCopy(GetDataPropertyCore(input)!, context));
        }

        /// <inheritdoc />
        public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, object value) where TBufferWriter : IBufferWriter<byte> => Serialize(ref writer, (Exception)value);

        /// <inheritdoc />
        public void Deserialize<TInput>(ref Reader<TInput> reader, object value) => Deserialize(ref reader, (Exception)value);
    }

    /// <summary>
    /// <see cref="AggregateException"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    internal sealed class AggregateExceptionCodec : GeneralizedReferenceTypeSurrogateCodec<AggregateException, AggregateExceptionSurrogate>
    {
        private readonly ExceptionCodec _baseCodec;

        /// <summary>
        /// 初始化 <see cref="AggregateExceptionCodec"/> 类的新实例。
        /// </summary>
        /// <param name="baseCodec">基类型编解码器。</param>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public AggregateExceptionCodec(ExceptionCodec baseCodec, IValueSerializer<AggregateExceptionSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
            _baseCodec = baseCodec;
        }

        /// <inheritdoc/>
        public override AggregateException ConvertFromSurrogate(ref AggregateExceptionSurrogate surrogate)
        {
            var result = new AggregateException(surrogate.InnerExceptions);
            var innerException = surrogate.InnerExceptions is { Count: > 0 } innerExceptions ? innerExceptions[0] : null;
            _baseCodec.SetBasePropertiesCore(result, surrogate.Message, surrogate.StackTrace, innerException, surrogate.HResult, surrogate.Data);
            return result;
        }

        /// <inheritdoc/>
        public override void ConvertToSurrogate(AggregateException value, ref AggregateExceptionSurrogate surrogate)
        {
            var info = _baseCodec.GetObjectDataCore(value);
            surrogate.Message = info.GetString("Message")!;
            surrogate.StackTrace = value.StackTrace;
            surrogate.HResult = value.HResult;
            var data = info.GetValue("Data", typeof(IDictionary));
            if (data is { })
            {
                surrogate.Data = ExceptionCodec.GetDataPropertyCore(value);
            }

            surrogate.InnerExceptions = value.InnerExceptions;
        }
    }

    /// <summary>
    /// <see cref="AggregateExceptionCodec"/> 的代理类型。
    /// </summary>
    [GenerateSerializer]
    internal struct AggregateExceptionSurrogate
    {
        [Id(0)]
        public string Message;

        [Id(1)]
        public string? StackTrace;

        [Id(2)]
        public Dictionary<object, object?>? Data;

        [Id(3)]
        public int HResult;

        [Id(4)]
        public ReadOnlyCollection<Exception> InnerExceptions;
    }
}
