using System;
using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs;

/// <summary>将原始弧字节序列化,同时保留源的所有权和内含。</summary>
/// <remarks>
/// 电话人拥有每个解码缓冲器,完成后予以处置。
/// 严格转让借用了这种所有权; <see cref="ArcBuffer.Slice(int)"/> 获得独立所有人。
/// </remarks>
[RegisterSerializer]
public sealed class ArcBufferCodec : IFieldCodec<ArcBuffer>
{
    /// <inheritdoc />
    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type? expectedType, ArcBuffer value)
        where TBufferWriter : IBufferWriter<byte>
    {
        value.CheckValidity();
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(ArcBuffer), WireType.LengthPrefixed);
        writer.WriteVarUInt32((uint)value.Length);
        foreach (var span in value) writer.Write(span);
    }

    /// <inheritdoc />
    public ArcBuffer ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        ReferenceCodec.MarkValueField(reader.Session);
        field.EnsureWireType(WireType.LengthPrefixed);
        var encodedLength = reader.ReadVarUInt32();
        if (encodedLength > int.MaxValue)
        {
            throw new IndexOutOfRangeException($"The declared ArcBuffer length, {encodedLength}, exceeds {int.MaxValue}.");
        }

        return reader.ReadOwnedBuffer((int)encodedLength);
    }
}

/// <summary>创建同一不可变字节上的独立所有人印记。</summary>
/// <remarks>调用者拥有返回的缓冲带,并独立于源处理。</remarks>
[RegisterCopier]
public sealed class ArcBufferCopier : IDeepCopier<ArcBuffer>
{
    /// <inheritdoc />
    public ArcBuffer DeepCopy(ArcBuffer input, CopyContext context) => input.Slice(0);
}
