using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
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
public class JsonCodec : IGeneralizedCodec, IGeneralizedCopier, ITypeFilter
{
    private static readonly Type SelfType = typeof(JsonCodec);
    private readonly ICodecSelector[] _serializableTypeSelectors;
    private readonly ICopierSelector[] _copyableTypeSelectors;
    private readonly JsonCodecOptions _options;

    /// <summary>
    /// 此编解码器的已知类型别名。
    /// </summary>
    public const string WellKnownAlias = "json";

    /// <summary>
    /// 初始化 <see cref="JsonCodec"/> 类的新实例。
    /// </summary>
    /// <param name="serializableTypeSelectors">用于指示此编解码器应序列化哪些类型的筛选器。</param>
    /// <param name="copyableTypeSelectors">用于指示此编解码器应复制哪些类型的筛选器。</param>
    /// <param name="options">JSON 编解码器选项。</param>
    public JsonCodec(
        IEnumerable<ICodecSelector> serializableTypeSelectors,
        IEnumerable<ICopierSelector> copyableTypeSelectors,
        IOptions<JsonCodecOptions> options)
    {
        if (serializableTypeSelectors is null) throw new ArgumentNullException(nameof(serializableTypeSelectors));
        if (copyableTypeSelectors is null) throw new ArgumentNullException(nameof(copyableTypeSelectors));
        if (options is null) throw new ArgumentNullException(nameof(options));

        _serializableTypeSelectors = serializableTypeSelectors.Where(t => string.Equals(t.CodecName, WellKnownAlias, StringComparison.Ordinal)).ToArray();
        _copyableTypeSelectors = copyableTypeSelectors.Where(t => string.Equals(t.CopierName, WellKnownAlias, StringComparison.Ordinal)).ToArray();
        _options = options.Value;
    }

    /// <inheritdoc/>
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
        // 注意，只要 Utf8JsonWriter 和 PooledBuffer 在每次使用结束时都被正确
        // 重置，就可以对它们使用池化机制。
        var bufferWriter = new BufferWriterBox<PooledBuffer>(new PooledBuffer());
        try
        {
            using var jsonWriter = new Utf8JsonWriter(bufferWriter, _options.WriterOptions);
            JsonSerializer.Serialize(jsonWriter, value, _options.SerializerOptions);
            jsonWriter.Flush();

            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteFieldHeaderExpected(1, WireType.LengthPrefixed);
            writer.WriteVarUInt32((uint)bufferWriter.Value.Length);
            bufferWriter.Value.CopyTo(ref writer);
        }
        finally
        {
            bufferWriter.Value.Dispose();
        }

        writer.WriteEndObject();
    }

    /// <inheritdoc/>
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

                    ReferenceCodec.MarkValueField(reader.Session);
                    var length = reader.ReadVarUInt32();

                    var tempBuffer = new PooledBuffer();
                    try
                    {
                        Utf8JsonReader jsonReader;
                        if (reader.TryReadBytes((int)length, out var span))
                        {
                            jsonReader = new Utf8JsonReader(span, _options.ReaderOptions);
                        }
                        else
                        {
                            reader.ReadBytes(ref tempBuffer, (int)length);
                            jsonReader = new Utf8JsonReader(tempBuffer.AsReadOnlySequence(), _options.ReaderOptions);
                        }

                        if (typeof(JsonNode).IsAssignableFrom(type!))
                        {
                            result = JsonNode.Parse(ref jsonReader);
                        }
                        else
                        {
                            result = JsonSerializer.Deserialize(ref jsonReader, type!, _options.SerializerOptions);
                        }
                    }
                    finally
                    {
                        tempBuffer.Dispose();
                    }

                    break;
                default:
                    reader.ConsumeUnknownField(header);
                    break;
            }
        }

        ReferenceCodec.RecordObject(reader.Session, result!, placeholderReferenceId);
        return result;
    }

    /// <inheritdoc/>
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

    private static bool IsNativelySupportedType(Type type)
    {
        // 添加 System.Text.Json 原生支持的类型。
        // 来源：https://github.com/dotnet/runtime/blob/2c4d0df3b146f8322f676b83a53ca21a065bdfc7/src/libraries/System.Text.Json/gen/JsonSourceGenerator.Parser.cs#L1792-L1797
        return type == typeof(JsonElement)
            || type == typeof(JsonDocument)
            || type == typeof(JsonArray)
            || type == typeof(JsonObject)
            || type == typeof(JsonValue)
            || typeof(JsonNode).IsAssignableFrom(type);
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    object? IDeepCopier.DeepCopy(object? input, CopyContext context)
    {
        if (context.TryGetCopy(input!, out object? result))
        {
            return result;
        }

        if (input is JsonNode jsonNode)
        {
            var copy = jsonNode.DeepClone();
            context.RecordCopy(input, copy);
            return copy;
        }

        var bufferWriter = new BufferWriterBox<PooledBuffer>(new PooledBuffer());
        try
        {
            using var jsonWriter = new Utf8JsonWriter(bufferWriter, _options.WriterOptions);
            JsonSerializer.Serialize(jsonWriter, input!, _options.SerializerOptions);
            jsonWriter.Flush();

            var sequence = bufferWriter.Value.AsReadOnlySequence();
            var jsonReader = new Utf8JsonReader(sequence, _options.ReaderOptions);
            result = JsonSerializer.Deserialize(ref jsonReader, input!.GetType(), _options.SerializerOptions)
                ?? throw new JsonException($"System.Text.Json returned null while deep copying an instance of type '{input.GetType()}'.");
        }
        finally
        {
            bufferWriter.Value.Dispose();
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

    /// <inheritdoc/>
    bool? ITypeFilter.IsTypeAllowed(Type type) => (((IGeneralizedCopier)this).IsSupportedType(type) || ((IGeneralizedCodec)this).IsSupportedType(type)) ? true : null;

    private static void ThrowTypeFieldMissing() => throw new RequiredFieldMissingException("Serialized value is missing its type field.");
}
