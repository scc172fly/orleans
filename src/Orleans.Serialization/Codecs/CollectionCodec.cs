using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization.Codecs;

/// <summary>
/// <see cref="Collection{T}"/>的序列器。
/// </summary>
/// <typeparam name="T">元素类型。</typeparam>
[RegisterSerializer]
public sealed class CollectionCodec<T> : IFieldCodec<Collection<T>>, IBaseCodec<Collection<T>>
{
    private readonly Type CodecElementType = typeof(T);

    private readonly IFieldCodec<T> _fieldCodec;

    /// <summary>
    /// 初始化 <see cref="CollectionCodec{T}"/> 类的新实例。
    /// </summary>
    /// <param name="fieldCodec">字段编码。</param>
    public CollectionCodec(IFieldCodec<T> fieldCodec)
    {
        _fieldCodec = OrleansGeneratedCodeHelper.UnwrapService(this, fieldCodec);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] Collection<T> value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }

        System.Diagnostics.Debug.Assert(value is not null);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, value.GetType(), WireType.TagDelimited);

        Serialize(ref writer, value);

        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    public Collection<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.WireType == WireType.Reference)
        {
            return ReferenceCodec.ReadReference<Collection<T>, TInput>(ref reader, field);
        }

        field.EnsureWireTypeTagDelimited();

        var placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        Collection<T>? result = null;
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
                    var length = (int)UInt32Codec.ReadValue(ref reader, header);
                    reader.EnsureAvailable((uint)length);

                    result = new Collection<T>(new List<T>(length));
                    ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
                    break;
                case 1:
                    if (result is null)
                    {
                        ThrowLengthFieldMissing();
                    }

                    result!.Add(_fieldCodec.ReadValue(ref reader, header)!);
                    break;
                default:
                    reader.ConsumeUnknownField(header);
                    break;
            }
        }

        if (result is null)
        {
            result = new();
            ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
        }

        return result!;
    }

    private void ThrowLengthFieldMissing() => throw new RequiredFieldMissingException("Serialized array is missing its length field.");

    /// <inheritdoc />
    public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, Collection<T> value) where TBufferWriter : IBufferWriter<byte>
    {
        ArgumentNullExceptionPolyfill.ThrowIfNull(value);

        if (value.Count > 0)
        {
            UInt32Codec.WriteField(ref writer, 0, (uint)value.Count);
            uint innerFieldIdDelta = 1;
            foreach (var element in value)
            {
                _fieldCodec.WriteField(ref writer, innerFieldIdDelta, CodecElementType, element);
                innerFieldIdDelta = 0;
            }
        }
    }

    /// <inheritdoc />
    public void Deserialize<TInput>(ref Reader<TInput> reader, Collection<T> value)
    {
        ArgumentNullExceptionPolyfill.ThrowIfNull(value);

        // 如果该值有建筑商添加的某种值,请清除这些值。
        // 如果这些数值在序列式有效载荷中,则将添加到下文。
        value.Clear();

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
                    var length = (int)UInt32Codec.ReadValue(ref reader, header);
                    reader.EnsureAvailable((uint)length);

                    break;
                case 1:
                    value.Add(_fieldCodec.ReadValue(ref reader, header)!);
                    break;
                default:
                    reader.ConsumeUnknownField(header);
                    break;
            }
        }
    }
}

/// <summary>
/// <see cref="Collection{T}"/>的复印机。
/// </summary>
/// <typeparam name="T">元素类型。</typeparam>
[RegisterCopier]
public sealed class CollectionCopier<T> : IDeepCopier<Collection<T>>, IBaseCopier<Collection<T>>
{
    private readonly IDeepCopier<T> _copier;

    /// <summary>
    /// 初始化 <see cref="CollectionCopier{T}"/> 类的新实例。
    /// </summary>
    /// <param name="valueCopier">价值复印机。</param>
    public CollectionCopier(IDeepCopier<T> valueCopier)
    {
        _copier = valueCopier;
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
    public Collection<T>? DeepCopy(Collection<T>? input, CopyContext context)
    {
        ArgumentNullExceptionPolyfill.ThrowIfNull(context);

        if (context.TryGetCopy<Collection<T>>(input, out var result))
        {
            return result!;
        }

        System.Diagnostics.Debug.Assert(input is not null);
        if (input.GetType() != typeof(Collection<T>))
        {
            return context.DeepCopy(input);
        }

        result = new Collection<T>(new List<T>(input.Count));
        context.RecordCopy(input, result);
        foreach (var item in input)
        {
            result.Add(_copier.DeepCopy(item, context)!);
        }

        return result;
    }

    /// <inheritdoc/>
    public void DeepCopy(Collection<T> input, Collection<T> output, CopyContext context)
    {
        ArgumentNullExceptionPolyfill.ThrowIfNull(input);
        ArgumentNullExceptionPolyfill.ThrowIfNull(output);
        ArgumentNullExceptionPolyfill.ThrowIfNull(context);

        output.Clear();

        foreach (var item in input)
        {
            output.Add(_copier.DeepCopy(item, context)!);
        }
    }
}
