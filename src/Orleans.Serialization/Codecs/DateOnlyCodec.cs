#if NET6_0_OR_GREATER
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs;

/// <summary>
/// <see cref="DateOnly"/>的序列器。
/// </summary>
[RegisterSerializer]
public sealed class DateOnlyCodec : IFieldCodec<DateOnly>
{
    void IFieldCodec<DateOnly>.WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] DateOnly value)
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(DateOnly), WireType.Fixed32);
        writer.WriteInt32(value.DayNumber);
    }

    /// <summary>
    /// 写入没有类型信息( 期待的类型为静态) 的字段。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, DateOnly value) where TBufferWriter : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeaderExpected(fieldIdDelta, WireType.Fixed32);
        writer.WriteInt32(value.DayNumber);
    }

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    DateOnly IFieldCodec<DateOnly>.ReadValue<TInput>(ref Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

    /// <summary>
    /// 读取 <see cref="DateOnly"/> 值。
    /// </summary>
    /// <typeparam name="TInput">读者输入类型。</typeparam>
    /// <param name="reader">读者</param>
    /// <param name="field">字段。</param>
    /// <returns>数值 <see cref="DateOnly"/> 。</returns>
    [return: System.Diagnostics.CodeAnalysis.MaybeNull]
    public static DateOnly ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        ReferenceCodec.MarkValueField(reader.Session);
        field.EnsureWireType(WireType.Fixed32);
        return DateOnly.FromDayNumber(reader.ReadInt32());
    }
}
#endif
