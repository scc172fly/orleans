using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// 底层类型为 32 位的枚举类型的序列化器。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <seealso cref="Orleans.Serialization.Codecs.IFieldCodec{T}" />
    public abstract class Enum32BaseCodec<T> : IFieldCodec<T> where T : unmanaged, Enum
    {
        private readonly Type CodecFieldType = typeof(T);

        /// <inheritdoc/>
        public unsafe void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, [System.Diagnostics.CodeAnalysis.AllowNull] Type expectedType, [System.Diagnostics.CodeAnalysis.AllowNull] T value) where TBufferWriter : IBufferWriter<byte>
        {
            HolderStruct holder;
            holder.Value = value;
            var intValue = *(int*)&holder;
            Int32Codec.WriteField(ref writer, fieldIdDelta, expectedType, intValue, CodecFieldType);
        }

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
        public unsafe T ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        {
            var intValue = Int32Codec.ReadValue(ref reader, field);
            return *(T*)&intValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HolderStruct
        {
            public T Value;
            public int Padding;
        }
    }

    /// <summary>
    /// <see cref="DateTimeKind"/> 的序列化器和复制器。
    /// </summary>
    [RegisterSerializer]
    internal sealed class DateTimeKindCodec : Enum32BaseCodec<DateTimeKind> { }

    /// <summary>
    /// <see cref="DayOfWeek"/> 的序列化器和复制器。
    /// </summary>
    [RegisterSerializer]
    internal sealed class DayOfWeekCodec : Enum32BaseCodec<DayOfWeek> { }
}
