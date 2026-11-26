using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Orleans.Serialization.Serializers;

/// <summary>
/// <typeparamref name="TField"/>的代用序列器。
/// </summary>
/// <typeparam name="TField">执行此类支持的类型。</typeparam>
/// <typeparam name="TSurrogate">代替代用类型 <typeparamref name="TField"/>.</typeparam>
/// <typeparam name="TConverter">转换器类型在 <typeparamref name="TField"/> 和 <typeparamref name="TSurrogate"/>.</typeparam>
public sealed class ValueTypeSurrogateCodec<TField, TSurrogate, TConverter>
    : IFieldCodec<TField>, IDeepCopier<TField>, IValueSerializer<TField>
    where TField : struct
    where TSurrogate : struct
    where TConverter : IConverter<TField, TSurrogate>
{
    private readonly IValueSerializer<TSurrogate> _surrogateSerializer;
    private readonly IDeepCopier<TSurrogate> _surrogateCopier;
    private readonly TConverter _converter;

    /// <summary>
    /// 初始化 <see cref="ValueTypeSurrogateCodec{TField, TSurrogate, TConverter}"/> 类的新实例。
    /// </summary>
    /// <param name="surrogateSerializer">代孕序列器</param>
    /// <param name="surrogateCopier">代孕复印机</param>
    /// <param name="converter">代用转换器。</param>
    public ValueTypeSurrogateCodec(
        IValueSerializer<TSurrogate> surrogateSerializer,
        IDeepCopier<TSurrogate> surrogateCopier,
        TConverter converter)
    {
        _surrogateSerializer = surrogateSerializer;
        _surrogateCopier = surrogateCopier;
        _converter = converter;
    }

    /// <inheritdoc/>
    public TField DeepCopy(TField input, CopyContext context)
    {
        var surrogate = _converter.ConvertToSurrogate(in input);
        var copy = _surrogateCopier.DeepCopy(surrogate, context);
        var result = _converter.ConvertFromSurrogate(in copy);

        return result;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deserialize<TInput>(ref Reader<TInput> reader, scoped ref TField value)
    {
        TSurrogate surrogate = default;
        _surrogateSerializer.Deserialize(ref reader, ref surrogate);
        value = _converter.ConvertFromSurrogate(in surrogate);
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    public TField ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        field.EnsureWireTypeTagDelimited();
        ReferenceCodec.MarkValueField(reader.Session);
        TField result = default;
        Deserialize(ref reader, ref result);
        return result;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Serialize<TBufferWriter>(ref Writer<TBufferWriter> writer, scoped ref TField value) where TBufferWriter : IBufferWriter<byte>
    {
        var surrogate = _converter.ConvertToSurrogate(in value);
        _surrogateSerializer.Serialize(ref writer, ref surrogate);
    }

    /// <inheritdoc/>
    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TField value) where TBufferWriter : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteStartObject(fieldIdDelta, expectedType, typeof(TField));
        Serialize(ref writer, ref value);
        writer.WriteEndObject();
    }
}
