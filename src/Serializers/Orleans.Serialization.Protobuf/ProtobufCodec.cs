using Google.Protobuf;
using Orleans.Metadata;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Orleans.Serialization;

/// <summary>
/// 为选定由 Protobuf 编解码器处理的 Protocol Buffers 消息类型提供序列化和深拷贝支持。
/// </summary>
[Alias(WellKnownAlias)]
public sealed class ProtobufCodec : IGeneralizedCodec, IGeneralizedCopier, ITypeFilter
{
    /// <summary>
    /// 用于选择 Protobuf 编解码器和复制器的已知别名。
    /// </summary>
    public const string WellKnownAlias = "protobuf";

    private static readonly Type SelfType = typeof(ProtobufCodec);
    private static readonly Type MessageType = typeof(IMessage);
    private static readonly Type MessageGenericType = typeof(IMessage<>);
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, MessageParser> MessageParsers = new();

    private readonly ICodecSelector[] _serializableTypeSelectors;
    private readonly ICopierSelector[] _copyableTypeSelectors;

    /// <summary>
    /// 初始化 <see cref="ProtobufCodec"/> 类的新实例。
    /// </summary>
    /// <param name="serializableTypeSelectors">用于指示此编解码器应序列化哪些类型的筛选器。</param>
    /// <param name="copyableTypeSelectors">用于指示此编解码器应复制哪些类型的筛选器。</param>
    public ProtobufCodec(
        IEnumerable<ICodecSelector> serializableTypeSelectors,
        IEnumerable<ICopierSelector> copyableTypeSelectors)
    {
        if (serializableTypeSelectors is null) throw new ArgumentNullException(nameof(serializableTypeSelectors));
        if (copyableTypeSelectors is null) throw new ArgumentNullException(nameof(copyableTypeSelectors));

        _serializableTypeSelectors = serializableTypeSelectors.Where(t => string.Equals(t.CodecName, WellKnownAlias, StringComparison.Ordinal)).ToArray();
        _copyableTypeSelectors = copyableTypeSelectors.Where(t => string.Equals(t.CopierName, WellKnownAlias, StringComparison.Ordinal)).ToArray();
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    public object? DeepCopy(object? input, CopyContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        if (!context.TryGetCopy(input!, out object? result))
        {
            if (input is not IMessage protobufMessage)
            {
                throw new InvalidOperationException("Input is not a protobuf message");
            }

            var messageSize = protobufMessage.CalculateSize();
            using var buffer = new PooledBuffer();
            var spanBuffer = buffer.GetSpan(messageSize)[..messageSize];
            protobufMessage.WriteTo(spanBuffer);

            result = protobufMessage.Descriptor.Parser.ParseFrom(spanBuffer);

            context.RecordCopy(input!, result);
        }

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
                return IsProtobufMessage(type);
            }
        }

        return false;
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
                return IsProtobufMessage(type);
            }
        }

        return false;
    }

    /// <inheritdoc/>
    bool? ITypeFilter.IsTypeAllowed(Type type)
    {
        if (!MessageType.IsAssignableFrom(type))
        {
            return null;
        }

        if (type == MessageType)
        {
            // 虽然 IMessage 是所有受支持类型的基础，但它本身不受直接支持。
            return null;
        }

        return ((IGeneralizedCodec)this).IsSupportedType(type) || ((IGeneralizedCopier)this).IsSupportedType(type);
    }

    private static bool IsProtobufMessage(Type type)
    {
        if (type == MessageType)
        {
            // 并非具体实现，因此不能直接序列化。
            return false;
        }

        if (type == MessageGenericType)
        {
            // 并非具体实现，但泛型类型确实提供了具体类型。
            type = type.GenericTypeArguments[0];
        }

        if (!MessageParsers.ContainsKey(type.TypeHandle))
        {
            if (Activator.CreateInstance(type) is not IMessage protobufMessageInstance)
            {
                return false;
            }

            MessageParsers.TryAdd(type.TypeHandle, protobufMessageInstance.Descriptor.Parser);
        }

        return true;
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

                    if (!MessageParsers.TryGetValue(type!.TypeHandle, out var messageParser))
                    {
                        throw new ArgumentException($"No parser found for the expected type {type.Name}", nameof(TInput));
                    }

                    ReferenceCodec.MarkValueField(reader.Session);
                    var encodedLength = reader.ReadVarUInt32();
                    if (encodedLength > int.MaxValue)
                    {
                        ThrowInvalidSizeException(encodedLength);
                    }

                    reader.EnsureAvailable(encodedLength);
                    var length = (int)encodedLength;

                    using (var buffer = new PooledBuffer())
                    {
                        var spanBuffer = buffer.GetSpan(length)[..length];
                        reader.ReadBytes(spanBuffer);
                        result = messageParser.ParseFrom(spanBuffer);
                    }
                    break;
                default:
                    reader.ConsumeUnknownField(header);
                    break;
            }
        }

        ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
        return result;
    }

    private static void ThrowTypeFieldMissing() => throw new RequiredFieldMissingException("Serialized value is missing its type field.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidSizeException(uint length) => throw new IndexOutOfRangeException(
        $"The declared protobuf payload length, {length}, exceeds the maximum supported length, {int.MaxValue}.");

    /// <inheritdoc/>
    void IFieldCodec.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [AllowNull] Type expectedType, [AllowNull] object? value)
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }

        if (value is not IMessage protobufMessage)
        {
            throw new ArgumentException("The provided value for serialization in not an instance of IMessage");
        }

        writer.WriteFieldHeader(fieldIdDelta, expectedType, SelfType, WireType.TagDelimited);

        // 写入类型名称。
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(0, WireType.LengthPrefixed);
        writer.Session.TypeCodec.WriteLengthPrefixed(ref writer, value.GetType());

        var messageSize = protobufMessage.CalculateSize();

        using var buffer = new PooledBuffer();
        var spanBuffer = buffer.GetSpan(messageSize)[..messageSize];

        // 写入序列化后的有效载荷。
        protobufMessage.WriteTo(spanBuffer);

        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(1, WireType.LengthPrefixed);
        writer.WriteVarUInt32((uint)spanBuffer.Length);
        writer.Write(spanBuffer);

        writer.WriteEndObject();
    }
}
