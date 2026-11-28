using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Serializers;

/// <summary>
/// <typeparamref name="TField"/> 的代理序列化器。
/// </summary>
/// <typeparam name="TField">此类的实现所支持的类型。</typeparam>
/// <typeparam name="TSurrogate">代替 <typeparamref name="TField"/> 进行序列化的代理类型。</typeparam>
/// <typeparam name="TConverter">在 <typeparamref name="TField"/> 和 <typeparamref name="TSurrogate"/> 之间执行转换的转换器类型。</typeparam>
public sealed class SurrogateCodec<TField, TSurrogate, TConverter>
    : IFieldCodec<TField>, IDeepCopier<TField>, IBaseCodec<TField>, IBaseCopier<TField>
    where TField : class
    where TSurrogate : struct
    where TConverter : IConverter<TField, TSurrogate>
{
    private readonly Type _fieldType = typeof(TField);
    private readonly IValueSerializer<TSurrogate> _surrogateSerializer;
    private readonly IDeepCopier<TSurrogate> _surrogateCopier;
    private readonly IPopulator<TField, TSurrogate>? _populator;
    private readonly TConverter _converter;

    /// <summary>
    /// 初始化 <see cref="SurrogateCodec{TField, TSurrogate, TConverter}"/> 类的新实例。
    /// </summary>
    /// <param name="surrogateSerializer">代理类型序列化器。</param>
    /// <param name="surrogateCopier">代理类型复制器。</param>
    /// <param name="converter">代理类型转换器。</param>
    public SurrogateCodec(
        IValueSerializer<TSurrogate> surrogateSerializer,
        IDeepCopier<TSurrogate> surrogateCopier,
        TConverter converter)
    {
        _surrogateSerializer = surrogateSerializer;
        _surrogateCopier = surrogateCopier;
        _converter = converter;
        _populator = converter as IPopulator<TField, TSurrogate>;
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    public TField? DeepCopy(TField? input, CopyContext context)
    {
        if (context.TryGetCopy<TField>(input, out var result))
        {
            return result!;
        }

        System.Diagnostics.Debug.Assert(input is not null);
        var surrogate = _converter.ConvertToSurrogate(in input);
        var copy = _surrogateCopier.DeepCopy(surrogate, context);
        result = _converter.ConvertFromSurrogate(in copy);

        context.RecordCopy(input, result);
        return result;
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    public TField ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.WireType == WireType.Reference)
        {
            return ReferenceCodec.ReadReference<TField, TInput>(ref reader, field);
        }

        if (field.FieldType is null || field.FieldType == _fieldType)
        {
            field.EnsureWireTypeTagDelimited();

            uint placeholderReferenceId = default;
            if (IsReferenceTrackingSupported)
            {
                placeholderReferenceId = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            }

            TSurrogate surrogate = default;
            _surrogateSerializer.Deserialize(ref reader, ref surrogate);
            var result = _converter.ConvertFromSurrogate(in surrogate);

            if (IsReferenceTrackingSupported)
            {
                ReferenceCodec.RecordObject(reader.Session, result, placeholderReferenceId);
            }

            return result;
        }

        return reader.DeserializeUnexpectedType<TInput, TField>(ref field);
    }

    /// <inheritdoc/>
    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
    {
        if (IsReferenceTrackingSupported && ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }

        if (value!.GetType() as object == _fieldType as object)
        {
            writer.WriteStartObject(fieldIdDelta, expectedType, _fieldType);
            var surrogate = _converter.ConvertToSurrogate(in value);
            _surrogateSerializer.Serialize(ref writer, ref surrogate);
            writer.WriteEndObject();
        }
        else
        {
            writer.SerializeUnexpectedType(fieldIdDelta, expectedType, value);
        }
    }

    private bool IsReferenceTrackingSupported => typeof(TField) != typeof(Exception) && !typeof(TField).IsSubclassOf(typeof(Exception));

    /// <inheritdoc/>
    public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, TField value) where TBufferWriter : IBufferWriter<byte>
    {
        if (_populator is null) ThrowNoPopulatorException();

        var surrogate = _converter.ConvertToSurrogate(in value);
        _surrogateSerializer.Serialize(ref writer, ref surrogate);
    }

    /// <inheritdoc/>
    public void Deserialize<TInput>(ref Reader<TInput> reader, TField value)
    {
        if (_populator is null) ThrowNoPopulatorException();

        TSurrogate surrogate = default;
        _surrogateSerializer.Deserialize(ref reader, ref surrogate);
        _populator.Populate(surrogate, value);
    }

    /// <inheritdoc/>
    public void DeepCopy(TField input, TField output, CopyContext context)
    {
        if (_populator is null) ThrowNoPopulatorException();

        var surrogate = _converter.ConvertToSurrogate(in input);
        var copy = _surrogateCopier.DeepCopy(surrogate, context);
        _populator.Populate(copy, output);
    }

    [DoesNotReturn]
    private void ThrowNoPopulatorException() => throw new NotSupportedException($"Surrogate type {typeof(TConverter)} does not implement {typeof(IPopulator<TField, TSurrogate>)} and therefore cannot be used in an inheritance hierarchy.");
}
