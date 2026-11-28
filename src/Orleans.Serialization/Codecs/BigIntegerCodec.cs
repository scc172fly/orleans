using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs;

/// <summary>
/// <see cref="BigInteger"/> 的序列化器。
/// </summary>
[RegisterSerializer]
public sealed class BigIntegerCodec : IFieldCodec<BigInteger>
{
    /// <inheritdoc/>
    void IFieldCodec<BigInteger>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] BigInteger value)
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(BigInteger), WireType.LengthPrefixed);

        WriteField(ref writer, value);
    }

    /// <summary>
    /// 写入字段而不写入类型信息（预期类型在静态分析时已知）。
    /// </summary>
    /// <typeparam name="TBufferWriter">缓冲区写入器类型。</typeparam>
    /// <param name="writer">写入器。</param>
    /// <param name="fieldIdDelta">字段标识符增量。</param>
    /// <param name="value">值。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, BigInteger value) where TBufferWriter : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.LengthPrefixed);

        WriteField(ref writer, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, BigInteger value)
        where TBufferWriter : IBufferWriter<byte>
    {
        var byteCount = value.GetByteCount();
        writer.WriteVarUInt32((uint)byteCount);

        writer.EnsureContiguous(byteCount);
        if (value.TryWriteBytes(writer.WritableSpan, out var bytesWritten))
        {
            writer.AdvanceSpan(bytesWritten);
        }
        else
        {
            writer.Write(value.ToByteArray());
        }
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    BigInteger IFieldCodec<BigInteger>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

    /// <summary>
    /// 读取一个值。
    /// </summary>
    /// <typeparam name="TInput">读取器的输入类型。</typeparam>
    /// <param name="reader">读取器。</param>
    /// <param name="field">字段。</param>
    /// <returns>值。</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    public static BigInteger ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        ReferenceCodec.MarkValueField(reader.Session);

        if (field.WireType != WireType.LengthPrefixed)
        {
            throw new UnexpectedLengthPrefixValueException(nameof(BigInteger), 0, 0);
        }

        var length = reader.ReadVarUInt32();
        var bytes = reader.ReadBytes(length);
        return new BigInteger(bytes);
    }
}
