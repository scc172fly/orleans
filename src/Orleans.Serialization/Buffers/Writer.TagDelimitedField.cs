using System;
using System.Runtime.CompilerServices;
using Orleans.Serialization.WireProtocol;

namespace Orleans.Serialization.Buffers
{
    public ref partial struct Writer<TBufferWriter>
    {
        /// <summary>
        /// 写入对象开始标记。
        /// </summary>
        /// <param name="fieldId">字段标识符。</param>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="actualType">实际类型。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStartObject(uint fieldId, Type? expectedType, Type actualType) => WriteFieldHeader(fieldId, expectedType, actualType, WireType.TagDelimited);

        /// <summary>
        /// 写入对象结束标记。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteEndObject() => WriteByte((byte)WireType.Extended | (byte)ExtendedWireType.EndTagDelimited);

        /// <summary>
        /// 写入基类型结束标记。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteEndBase() => WriteByte((byte)WireType.Extended | (byte)ExtendedWireType.EndBaseFields);
    }
}
