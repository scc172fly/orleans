using System.Runtime.CompilerServices;

namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 序列化标记，大小始终恰好为一个字节。它是所有序列化字段的字段头的一部分。
    /// </summary>
    /// <remarks>
    /// 标记字节的典型格式为 <c>[W W W] [S S] [F F F]</c>，其中每个字母表示一个二进制位。
    /// W 表示 <see cref="WireType"/> 位，S 表示 <see cref="SchemaType"/> 位，F 表示字段标识符位。
    /// </remarks>
    public struct Tag
    {
        /// <summary>
        /// 传输格式类型掩码。
        /// </summary>
        public const byte WireTypeMask = 0b1110_0000; // 前 3 位用于传输格式类型。

        /// <summary>
        /// 架构类型掩码。
        /// </summary>
        public const byte SchemaTypeMask = 0b0001_1000; // 如果预期存在架构类型，则接下来的 2 位用于架构类型说明符。

        /// <summary>
        /// 字段标识符掩码。
        /// </summary>
        public const byte FieldIdMask = 0b0000_0111; // 如果预期存在字段 ID 差值，则最后 3 位用于该差值。

        /// <summary>
        /// 完整字段标识符掩码。
        /// </summary>
        public const byte FieldIdCompleteMask = FieldIdMask;

        /// <summary>
        /// 扩展传输格式类型掩码。
        /// </summary>
        public const byte ExtendedWireTypeMask = 0b0001_1000;

        /// <summary>
        /// 可嵌入的最大字段 ID 差值。
        /// </summary>
        public const int MaxEmbeddedFieldIdDelta = 6;

        private uint _tag;

        /// <summary>
        /// 初始化 <see cref="Tag"/> 结构的新实例。
        /// </summary>
        /// <param name="tag">标记字节。</param>
        public Tag(byte tag) => _tag = tag;

        internal Tag(uint tag) => _tag = tag;

        /// <summary>
        /// 执行从 <see cref="byte"/> 到 <see cref="Tag"/> 的隐式转换。
        /// </summary>
        /// <param name="tag">标记。</param>
        /// <returns>转换结果。</returns>
        public static implicit operator Tag(byte tag) => new(tag);

        /// <summary>
        /// 执行从 <see cref="Tag"/> 到 <see cref="byte"/> 的隐式转换。
        /// </summary>
        /// <param name="tag">标记。</param>
        /// <returns>转换结果。</returns>
        public static implicit operator byte(Tag tag) => (byte)tag._tag;

        /// <summary>
        /// 获取或设置此标记之后的数据的传输格式类型。
        /// </summary>
        public WireType WireType
        {
            readonly get => (WireType)(_tag & WireTypeMask);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)WireTypeMask) | ((uint)value & WireTypeMask);
        }

        /// <summary>
        /// 获取一个值，指示此实例是否具有扩展传输格式类型。
        /// </summary>
        /// <value><see langword="true" />，如果此实例具有扩展传输格式类型；否则为 <see langword="false" />。</value>
        public readonly bool HasExtendedWireType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _tag >= (byte)WireType.Extended; //(this.tag & (byte) WireType.Extended) == (byte) WireType.Extended;
        }

        /// <summary>
        /// 获取或设置此标记之后的数据的扩展传输格式类型。
        /// </summary>
        public ExtendedWireType ExtendedWireType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (ExtendedWireType)(_tag & ExtendedWireTypeMask);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)ExtendedWireTypeMask) | ((uint)value & ExtendedWireTypeMask);
        }

        internal readonly bool IsEndBaseFields => _tag == ((byte)WireType.Extended | (byte)ExtendedWireType.EndBaseFields);

        internal readonly bool IsEndObject => _tag == ((byte)WireType.Extended | (byte)ExtendedWireType.EndTagDelimited);

        /// <summary>
        /// 获取或设置架构类型。
        /// </summary>
        public SchemaType SchemaType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (SchemaType)(_tag & SchemaTypeMask);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)SchemaTypeMask) | ((uint)value & SchemaTypeMask);
        }

        /// <summary>
        /// 获取一个值，指示 <see cref="SchemaType" /> 属性是否有效。
        /// </summary>
        /// <value><see langword="true"/>，如果 <see cref="SchemaType"/> 有效；否则为 <see langword="false"/>。</value>
        public readonly bool IsSchemaTypeValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !HasExtendedWireType; //(this.tag & (byte) WireType.Extended) != (byte) WireType.Extended;
        }

        /// <summary>
        /// 返回此标记所表示字段的 <see cref="FieldIdDelta"/>。
        /// </summary>
        /// <remarks>
        /// 如果 <see cref="IsFieldIdValid"/> 为 <see langword="false"/>，则此值不是完整的字段 ID 差值。
        /// </remarks>
        public uint FieldIdDelta
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => _tag & FieldIdMask;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)FieldIdMask) | (value & FieldIdMask);
        }

        /// <summary>
        /// 将 <see cref="FieldIdDelta"/> 标记为无效。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFieldIdInvalid() => _tag |= FieldIdCompleteMask;

        /// <summary>
        /// 获取一个值，指示 <see cref="FieldIdDelta"/> 属性是否有效。
        /// </summary>
        /// <value>
        /// <see langword="true"/>，如果 <see cref="FieldIdDelta"/> 表示完整 ID；如果还需要更多数据，则为 <see langword="false"/>。
        /// </value>
        /// <remarks>
        /// 如果标记中字段 ID 部分的所有位均为 1，则此字段 ID 无效，标记后必须跟随字段 ID。
        /// 因此，无需额外字节即可表示 0 至 6 的字段 ID。
        /// </remarks>
        public readonly bool IsFieldIdValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_tag & FieldIdCompleteMask) != FieldIdCompleteMask && !HasExtendedWireType;
        }

        /// <summary>
        /// 获取一个值，指示标记后是否跟随扩展字段 ID。
        /// </summary>
        public readonly bool HasExtendedFieldId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_tag & FieldIdCompleteMask) == FieldIdCompleteMask && !HasExtendedWireType;
        }
    }
}
