using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Runtime;

/// <summary>
/// 提供 <see cref="IdSpan"/> 实例的序列化和反序列化功能。
/// </summary>
[RegisterSerializer]
public sealed class IdSpanCodec : IFieldCodec<IdSpan>
{
    private readonly Type _codecType = typeof(IdSpan);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteField<TBufferWriter>(
        ref Writer<TBufferWriter> writer,
        uint fieldIdDelta,
        [AllowNull] Type expectedType,
        IdSpan value)
        where TBufferWriter : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, _codecType, WireType.LengthPrefixed);
        var bytes = value.AsSpan();
        if (bytes.IsEmpty) writer.WriteByte(1); // 等效于 `writer.WriteVarUInt32(0);`。
        else
        {
            writer.WriteVarUInt32((uint)(sizeof(int) + bytes.Length));
            writer.WriteInt32(value.GetHashCode());
            writer.Write(bytes);
        }
    }

    /// <summary>
    /// 将 <see cref="IdSpan"/> 值写入提供的写入器，不写入字段帧结构。
    /// </summary>
    /// <param name="writer">写入器。</param>
    /// <param name="value">要写入的值。</param>
    /// <typeparam name="TBufferWriter">底层缓冲区写入器类型。</typeparam>
    public static void WriteRaw<TBufferWriter>(
        ref Writer<TBufferWriter> writer,
        IdSpan value)
        where TBufferWriter : IBufferWriter<byte>
    {
        var bytes = value.AsSpan();
        writer.WriteVarUInt32((uint)bytes.Length);
        if (!bytes.IsEmpty)
        {
            writer.WriteInt32(value.GetHashCode());
            writer.Write(bytes);
        }
    }

    /// <summary>
    /// 从读取器中读取 <see cref="IdSpan"/> 值，不读取任何字段帧结构。
    /// </summary>
    /// <typeparam name="TInput">底层读取器输入类型。</typeparam>
    /// <param name="reader">读取器。</param>
    /// <returns><see cref="IdSpan"/> 值。</returns>
    public static unsafe IdSpan ReadRaw<TInput>(ref Reader<TInput> reader)
    {
        var length = reader.ReadVarUInt32();
        if (length == 0)
            return default;

        var hashCode = reader.ReadInt32();
        var payloadArray = reader.ReadBytes(length);
        return IdSpan.UnsafeCreate(payloadArray, hashCode);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IdSpan ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        field.EnsureWireType(WireType.LengthPrefixed);
        ReferenceCodec.MarkValueField(reader.Session);

        var length = reader.ReadVarUInt32();
        if (length == 0)
            return default;

        var hashCode = reader.ReadInt32();
        var payloadArray = reader.ReadBytes(length - sizeof(int));
        return IdSpan.UnsafeCreate(payloadArray, hashCode);
    }
}
