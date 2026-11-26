namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 代表3位线型,移到位置 
    /// </summary>
    public enum WireType : uint
    {
        /// <summary>
        /// 变量长整数值。
        /// </summary>
        /// <remarks>        
        /// 之后是可变长整数。 
        /// </remarks>        
        VarInt = 0b000 << 5,

        /// <summary>
        /// 由一组带标签的限定字段组成的复合值。
        /// </summary>
        /// <remarks>        
        /// 之后是字段规格,然后是 <see cref="WireType.Extended"/> 标签标签 <see cref="ExtendedWireType.EndTagDelimited"/> 以扩展线型。 
        /// </remarks>        
        TagDelimited = 0b001 << 5,

        /// <summary>
        /// 长度前置值。
        /// </summary>
        /// <remarks>        
        /// 之后是 VarInt 长度,代表随后的字节数。 
        /// </remarks>        
        LengthPrefixed = 0b010 << 5,

        /// <summary>
        /// 32位数
        /// </summary>
        /// <remarks>        
        /// 之后是4字节。
        /// </remarks>        
        Fixed32 = 0b011 << 5,

        /// <summary>
        /// 64位数
        /// </summary>
        /// <remarks>        
        /// 之后是8字节。
        /// </remarks>        
        Fixed64 = 0b100 << 5,

        /// <summary>
        /// 引用先前编码值。
        /// </summary>
        /// <remarks>        
        /// 之后是8字节。
        /// </remarks>        
        Reference = 0b110 << 5, // 之后, VarInt 提及先前定义的对象。请注意,必须仍然包括 SchemaType 和类型规格。

        /// <summary>
        /// 控制标签,其剩余位数含有 <see cref="ExtendedWireType"/> 值。
        /// </summary>
        Extended = 0b111 << 5, // 这是控制标记。 系统图类型和嵌入字段代号无效。 其余的5位元用于控制信息 。
    }
}
