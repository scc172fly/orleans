using System;
using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs;

/// <summary>序列化 Arc 的原始字节，同时保留源缓冲区的所有权和内容。</summary>
/// <remarks>
/// 调用方拥有每个解码后的缓冲区，并在使用完毕后释放它。
/// 结构体赋值只是借用该所有权；<see cref="ArcBuffer.Slice(int)"/> 获取独立的所有权。
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

/// <summary>在相同的不可变字节上创建独立的所有权固定引用。</summary>
/// <remarks>调用方拥有返回的缓冲区，并独立于源缓冲区释放它。</remarks>
[RegisterCopier]
public sealed class ArcBufferCopier : IDeepCopier<ArcBuffer>
{
    /// <inheritdoc />
    public ArcBuffer DeepCopy(ArcBuffer input, CopyContext context) => input.Slice(0);
}
