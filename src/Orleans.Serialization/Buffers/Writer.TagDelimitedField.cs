using System;
using System.Runtime.CompilerServices;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Buffers
{
    public ref partial struct Writer<TBufferWriter>
    {
        /// <summary>
        /// 写入起始对象标签。
        /// </summary>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预想的类型。</param>
        /// <param name="actualType">实际类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStartObject(uint fieldId, Type? expectedType, Type actualType) => WriteFieldHeader(fieldId, expectedType, actualType, WireType.TagDelimited);

        /// <summary>
        /// 写入最终对象标记。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteEndObject() => WriteByte((byte)WireType.Extended | (byte)ExtendedWireType.EndTagDelimited);

        /// <summary>
        /// 写最后基本标记。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteEndBase() => WriteByte((byte)WireType.Extended | (byte)ExtendedWireType.EndBaseFields);
    }
}
