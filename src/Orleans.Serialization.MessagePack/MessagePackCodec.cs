using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using MessagePack;
using Microsoft.Extensions.Options;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization;

/// <summary>
/// 使用 <see cref="MessagePackSerializer"/> 的序列化编解码器。
/// </summary>
/// <remarks>
/// MessagePack 编解码器的性能略逊于默认 Orleans 序列化器。如果性能对应用程序至关重要，请考虑使用默认序列化机制。
/// </remarks>
[Alias(WellKnownAlias)]
public class MessagePackCodec : IGeneralizedCodec, IGeneralizedCopier, ITypeFilter
{
    private static readonly ConcurrentDictionary<Type, bool> SupportedTypes = new();

    private static readonly Type SelfType = typeof(MessagePackCodec);

    private readonly ICodecSelector[] _serializableTypeSelectors;
    private readonly ICopierSelector[] _copyableTypeSelectors;
    private readonly MessagePackCodecOptions _options;

    /// <summary>
    /// 此编解码器的已知类型别名。
    /// </summary>
    public const string WellKnownAlias = "msgpack";

    /// <summary>
    /// 初始化 <see cref="MessagePackCodec"/> 类的新实例。
    /// </summary>
    /// <param name="serializableTypeSelectors">用于指示此编解码器应序列化哪些类型的筛选器。</param>
    /// <param name="copyableTypeSelectors">用于指示此编解码器应复制哪些类型的筛选器。</param>
    /// <param name="options">MessagePack 编解码器选项。</param>
    public MessagePackCodec(
        IEnumerable<ICodecSelector> serializableTypeSelectors,
        IEnumerable<ICopierSelector> copyableTypeSelectors,
        IOptions<MessagePackCodecOptions> options)
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
        writer.WriteFieldHeader(fieldIdDelta, expectedType, SelfType, WireType.TagDelimited);

        // 写入类型名称。
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(0, WireType.LengthPrefixed);
        writer.Session.TypeCodec.WriteLengthPrefixed(ref writer, value.GetType());

        var bufferWriter = new BufferWriterBox<PooledBuffer>(new());
        try
        {

            var msgPackWriter = new MessagePackWriter(bufferWriter);
            MessagePackSerializer.Serialize(value.GetType(), ref msgPackWriter, value, _options.SerializerOptions);
            msgPackWriter.Flush();

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

                    var bufferWriter = new BufferWriterBox<PooledBuffer>(new());
                    try
                    {
                        reader.ReadBytes(ref bufferWriter, (int)length);
                        result = MessagePackSerializer.Deserialize(type!, bufferWriter.Value.AsReadOnlySequence(), _options.SerializerOptions);
                    }
                    finally
                    {
                        bufferWriter.Value.Dispose();
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

        return IsMessagePackContract(type, _options.AllowDataContractAttributes);
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    object? IDeepCopier.DeepCopy(object? input, CopyContext context)
    {
        if (context.TryGetCopy(input!, out object? result))
        {
            return result;
        }

        var bufferWriter = new BufferWriterBox<PooledBuffer>(new());
        try
        {
            var msgPackWriter = new MessagePackWriter(bufferWriter);
            MessagePackSerializer.Serialize(input!.GetType(), ref msgPackWriter, input, _options.SerializerOptions);
            msgPackWriter.Flush();

            var sequence = bufferWriter.Value.AsReadOnlySequence();
            result = MessagePackSerializer.Deserialize(input.GetType(), sequence, _options.SerializerOptions)
                ?? throw new SerializationException($"MessagePack returned null while deep copying an instance of type '{input.GetType()}'.");
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

        return IsMessagePackContract(type, _options.AllowDataContractAttributes);
    }

    /// <inheritdoc/>
    bool? ITypeFilter.IsTypeAllowed(Type type) => (((IGeneralizedCopier)this).IsSupportedType(type) || ((IGeneralizedCodec)this).IsSupportedType(type)) ? true : null;

    private static bool IsMessagePackContract(Type type, bool allowDataContractAttribute)
    {
        if (SupportedTypes.TryGetValue(type, out bool isMsgPackContract))
        {
            return isMsgPackContract;
        }

        isMsgPackContract = type.GetCustomAttribute<MessagePackObjectAttribute>() is not null;

        if (!isMsgPackContract && allowDataContractAttribute)
        {
            isMsgPackContract = type.GetCustomAttribute<DataContractAttribute>() is DataContractAttribute;
        }

        SupportedTypes.TryAdd(type, isMsgPackContract);
        return isMsgPackContract;
    }

    private static void ThrowTypeFieldMissing() => throw new RequiredFieldMissingException("Serialized value is missing its type field.");
}
