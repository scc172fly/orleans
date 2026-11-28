using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans.Metadata;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization;

/// <summary>
/// 使用 <see cref="JsonSerializer"/> 的序列化编解码器。
/// </summary>
[Alias(WellKnownAlias)]
public class NewtonsoftJsonCodec : IGeneralizedCodec, IGeneralizedCopier, ITypeFilter
{
    private static readonly Type SelfType = typeof(NewtonsoftJsonCodec);
    private readonly NewtonsoftJsonCodecOptions _options;
    private readonly ICodecSelector[] _serializableTypeSelectors;
    private readonly ICopierSelector[] _copyableTypeSelectors;
    private readonly JsonSerializer _serializer;

    /// <summary>
    /// 此编解码器的已知类型别名。
    /// </summary>
    public const string WellKnownAlias = "json.net";

    /// <summary>
    /// 初始化 <see cref="NewtonsoftJsonCodec"/> 类的新实例。
    /// </summary>
    /// <param name="serializableTypeSelectors">用于指示此编解码器应序列化哪些类型的筛选器。</param>
    /// <param name="copyableTypeSelectors">用于指示此编解码器应复制哪些类型的筛选器。</param>
    /// <param name="options">JSON 编解码器选项。</param>
    public NewtonsoftJsonCodec(
        IEnumerable<ICodecSelector> serializableTypeSelectors,
        IEnumerable<ICopierSelector> copyableTypeSelectors,
        IOptions<NewtonsoftJsonCodecOptions> options)
    {
        if (serializableTypeSelectors is null) throw new ArgumentNullException(nameof(serializableTypeSelectors));
        if (copyableTypeSelectors is null) throw new ArgumentNullException(nameof(copyableTypeSelectors));
        if (options is null) throw new ArgumentNullException(nameof(options));

        _options = options.Value;
        _serializableTypeSelectors = serializableTypeSelectors.Where(t => string.Equals(t.CodecName, WellKnownAlias, StringComparison.Ordinal)).ToArray();
        _copyableTypeSelectors = copyableTypeSelectors.Where(t => string.Equals(t.CopierName, WellKnownAlias, StringComparison.Ordinal)).ToArray();
        _serializer = JsonSerializer.Create(_options.SerializerSettings);
    }

    void IFieldCodec.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [AllowNull] Type expectedType, [AllowNull] object? value)
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }

        // 序列化字段时，架构类型为该编解码器的类型。
        // 实际上，只要此编解码器注册为处理程序，该类型可以是任何唯一的类型。
        // 通过在 IsSupportedType 中检查编解码器类型，也可以只将该编解码器注册为 IGenericCodec。
        // 注意，编解码器负责序列化值本身的类型。
        writer.WriteFieldHeader(fieldIdDelta, expectedType, SelfType, WireType.TagDelimited);

        // 写入类型名称。
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(0, WireType.LengthPrefixed);
        writer.Session.TypeCodec.WriteLengthPrefixed(ref writer, value.GetType());

        // 写入序列化后的有效载荷。
        var serializedValue = JsonConvert.SerializeObject(value, _options.SerializerSettings);
        StringCodec.WriteField(ref writer, 1, serializedValue);

        writer.WriteEndObject();
    }

    object? IFieldCodec.ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.IsReference)
        {
            return ReferenceCodec.ReadReference(ref reader, field.FieldType);
        }

        field.EnsureWireTypeTagDelimited();

        var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        object? result = null;
        Type? type = null;
        uint fieldId = 0;
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
                    ReferenceCodec.MarkValueField(reader.Session);
                    type = reader.Session.TypeCodec.ReadLengthPrefixed(ref reader);
                    break;
                case 1:
                    if (type is null)
                    {
                        ThrowTypeFieldMissing();
                    }

                    // 为了可能提高效率，可将此处改为读取 ReadOnlySequence<byte> 而不是字节数组。
                    var serializedValue = StringCodec.ReadValue(ref reader, header);
                    result = JsonConvert.DeserializeObject(serializedValue!, type, _options.SerializerSettings);
                    ReferenceCodec.RecordObject(reader.Session, result!, placeholderReferenceId);
                    break;
                default:
                    reader.ConsumeUnknownField(header);
                    break;
            }
        }

        return result;
    }

    bool IGeneralizedCodec.IsSupportedType(Type type)
    {
        if (type == SelfType)
        {
            return true;
        }

        if (IsNativelySupportedType(type))
        {
            return true;
        }

        if (CommonCodecTypeFilter.IsAbstractOrFrameworkType(type))
        {
            return false;
        }

        foreach (var selector in _serializableTypeSelectors)
        {
            if (selector.IsSupportedType(type))
            {
                return true;
            }
        }

        if (_options.IsSerializableType?.Invoke(type) is bool value)
        {
            return value;
        }

        return false;
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    object? IDeepCopier.DeepCopy(object? input, CopyContext context)
    {
        if (context.TryGetCopy(input!, out object? result))
            return result;

        var stream = PooledBufferStream.Rent();
        try
        {
            var type = input!.GetType();
            var streamWriter = new StreamWriter(stream);
            using (var textWriter = new JsonTextWriter(streamWriter) { CloseOutput = false })
                _serializer.Serialize(textWriter, input, type);
            streamWriter.Flush();

            stream.Position = 0;

            var streamReader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(streamReader);
            result = _serializer.Deserialize(jsonReader, type)
                ?? throw new JsonSerializationException($"Newtonsoft.Json returned null while deep copying an instance of type '{type}'.");
        }
        finally
        {
            PooledBufferStream.Return(stream);
        }

        context.RecordCopy(input!, result);
        return result;
    }

    /// <inheritdoc/>
    bool IGeneralizedCopier.IsSupportedType(Type type)
    {
        if (IsNativelySupportedType(type))
        {
            return true;
        }

        if (CommonCodecTypeFilter.IsAbstractOrFrameworkType(type))
        {
            return false;
        }

        foreach (var selector in _copyableTypeSelectors)
        {
            if (selector.IsSupportedType(type))
            {
                return true;
            }
        }

        if (_options.IsCopyableType?.Invoke(type) is bool value)
        {
            return value;
        }

        return false;
    }

    private static bool IsNativelySupportedType(Type type)
    {
        return type == typeof(JObject)
                    || type == typeof(JArray)
                    || type == typeof(JProperty)
                    || type == typeof(JRaw)
                    || type == typeof(JValue)
                    || type == typeof(JConstructor)
                    || typeof(JContainer).IsAssignableFrom(type)
                    || typeof(JToken).IsAssignableFrom(type);
    }

    private static void ThrowTypeFieldMissing() => throw new RequiredFieldMissingException("Serialized value is missing its type field.");

    private bool IsSupportedType(Type type) => ((IGeneralizedCodec)this).IsSupportedType(type) || ((IGeneralizedCopier)this).IsSupportedType(type);
    bool? ITypeFilter.IsTypeAllowed(Type type) => IsSupportedType(type) ? true : null;
}
