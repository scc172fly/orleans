#if NET6_0_OR_GREATER
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs;

/// <summary>
/// <see cref="TimeOnly"/> 的序列化器。
/// </summary>
[RegisterSerializer]
public sealed class TimeOnlyCodec : IFieldCodec<TimeOnly>
{
    void IFieldCodec<TimeOnly>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] TimeOnly value)
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(TimeOnly), WireType.Fixed64);
        writer.WriteInt64(value.Ticks);
    }

    /// <summary>
    /// 写入字段而不写入类型信息（预期类型已静态确定）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, TimeOnly value) where TBufferWriter : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.Fixed64);
        writer.WriteInt64(value.Ticks);
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    TimeOnly IFieldCodec<TimeOnly>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

    /// <summary>
    /// 读取 <see cref="TimeOnly"/> 值。
    /// </summary>
    /// <typeparam name="TInput">读取器的输入类型。</typeparam>
    /// <param name="reader">读取器。</param>
    /// <param name="field">字段。</param>
    /// <returns><see cref="TimeOnly"/> 值。</returns>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    public static TimeOnly ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        ReferenceCodec.MarkValueField(reader.Session);
        field.EnsureWireType(WireType.Fixed64);
        return new TimeOnly(reader.ReadInt64());
    }
}
#endif
