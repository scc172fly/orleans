using System.Runtime.CompilerServices;

namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 序列标记, 它总是一个字节。 它作为所有序列字段的字段页眉的一部分 。
    /// </summary>
    /// <remarks>
    /// 标签字节的典型形式是 <c>[W W W] [S S] [F F F]</c>,其中每个字节都有一点。
    /// W 是 <see cref="WireType"/>, S 是 <see cref="SchemaType"/> 位数, F 是字段标识符位。
    /// </remarks>
    public struct Tag
    {
        /// <summary>
        /// 铁丝网面具
        /// </summary>
        public const byte WireTypeMask = 0b1110_0000; // 前三位数是用线型的

        /// <summary>
        /// 假面具
        /// </summary>
        public const byte SchemaTypeMask = 0b0001_1000; // 如果预计会使用化学型态, 则接下来的 2 位数将专门用于化学型态的指定符。

        /// <summary>
        /// 字段识别面具 。
        /// </summary>
        public const byte FieldIdMask = 0b0000_0111; // 如果预计会达到三角洲,则最后的3位数用于实地的 id 三角洲。

        /// <summary>
        /// 字段识别码完整掩码。
        /// </summary>
        public const byte FieldIdCompleteMask = FieldIdMask;

        /// <summary>
        /// 长线型面具
        /// </summary>
        public const byte ExtendedWireTypeMask = 0b0001_1000;

        /// <summary>
        /// 最大嵌入字段识别符 delta 。
        /// </summary>
        public const int MaxEmbeddedFieldIdDelta = 6;

        private uint _tag;

        /// <summary>
        /// 初始化 <see cref="Tag"/> 结构的新实例。
        /// </summary>
        /// <param name="tag">标签字节。</param>
        public Tag(byte tag) => _tag = tag;

        internal Tag(uint tag) => _tag = tag;

        /// <summary>
        /// 执行从 <see cref="byte"/> 到 <see cref="Tag"/>的隐含转换。
        /// </summary>
        /// <param name="tag">标签。</param>
        /// <returns>转换结果。</returns>
        public static implicit operator Tag(byte tag) => new(tag);

        /// <summary>
        /// 执行从 <see cref="Tag"/> 到 <see cref="byte"/>的隐含转换。
        /// </summary>
        /// <param name="tag">标签。</param>
        /// <returns>转换结果。</returns>
        public static implicit operator byte(Tag tag) => (byte)tag._tag;

        /// <summary>
        /// 获取或设置此标签后的数据的线型。
        /// </summary>
        public WireType WireType
        {
            readonly get => (WireType)(_tag & WireTypeMask);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)WireTypeMask) | ((uint)value & WireTypeMask);
        }

        /// <summary>
        /// 获得一个值来显示此实例是否有长线型。
        /// </summary>
        /// <value><see langword="true" /> 如果这种情况有长线型;否则, <see langword="false" />。</value>
        public readonly bool HasExtendedWireType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _tag >= (byte)WireType.Extended; //(this.tag ( 位数) (字节) WireType.Extended) == (byte) WireType.Extended;
        }

        /// <summary>
        /// 获取或设置此标签后的数据的扩展线类型。
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
        /// 设置或设置模式类型。
        /// </summary>
        public SchemaType SchemaType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (SchemaType)(_tag & SchemaTypeMask);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)SchemaTypeMask) | ((uint)value & SchemaTypeMask);
        }

        /// <summary>
        /// 获得一个值,以显示 <see cref="SchemaType" /> 属性是否有效。
        /// </summary>
        /// <value><see langword="true"/> 如果 <see cref="SchemaType"/> 有效, <see langword="false"/> 否则。</value>
        public readonly bool IsSchemaTypeValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !HasExtendedWireType; //(this.tag ( 位数) (字节) WireType.Extended$= (字节) WireType.Extended;
        }

        /// <summary>
        /// 返回此标记所代表的字段的 <see cref="FieldIdDelta"/> 。
        /// </summary>
        /// <remarks>
        /// 如果 <see cref="IsFieldIdValid"/> 是 <see langword="false"/>,则该值不是一个完整的字段 id 三角洲。
        /// </remarks>
        public uint FieldIdDelta
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => _tag & FieldIdMask;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _tag = (_tag & ~(uint)FieldIdMask) | (value & FieldIdMask);
        }

        /// <summary>
        /// 无效 <see cref="FieldIdDelta"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFieldIdInvalid() => _tag |= FieldIdCompleteMask;

        /// <summary>
        /// 获得一个值,以显示 <see cref="FieldIdDelta"/> 属性是否有效。
        /// </summary>
        /// <value>
        /// <see langword="true"/> 如果 <see cref="FieldIdDelta"/> 表示一个完整的id, <see langword="false"/> 如果需要更多数据。
        /// </value>
        /// <remarks>
        /// 如果标记的字段 ID 部分中设置了所有位元, 此字段 id 无效, 此标签必须有一个字段 id 。
        /// 因此,无需增加字节即可代表字段编号0-6。
        /// </remarks>
        public readonly bool IsFieldIdValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_tag & FieldIdCompleteMask) != FieldIdCompleteMask && !HasExtendedWireType;
        }

        /// <summary>
        /// 获得一个值,显示标签是否后面有一个扩展字段代号。
        /// </summary>
        public readonly bool HasExtendedFieldId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_tag & FieldIdCompleteMask) == FieldIdCompleteMask && !HasExtendedWireType;
        }
    }
}
