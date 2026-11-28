namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 表示移位到指定位置的 3 位传输格式类型。
    /// </summary>
    public enum WireType : uint
    {
        /// <summary>
        /// 变长整数值。
        /// </summary>
        /// <remarks>        
        /// 后面跟随一个变长整数。
        /// </remarks>        
        VarInt = 0b000 << 5,

        /// <summary>
        /// 由一组标记分隔字段组成的复合值。
        /// </summary>
        /// <remarks>        
        /// 后面跟随字段说明符，最后是 <see cref="WireType.Extended"/> 标记，其扩展传输格式类型为 <see cref="ExtendedWireType.EndTagDelimited"/>。
        /// </remarks>        
        TagDelimited = 0b001 << 5,

        /// <summary>
        /// 带长度前缀的值。
        /// </summary>
        /// <remarks>        
        /// 后面跟随以 VarInt 编码的长度，表示之后的字节数。
        /// </remarks>        
        LengthPrefixed = 0b010 << 5,

        /// <summary>
        /// 32 位值。
        /// </summary>
        /// <remarks>        
        /// 后面跟随 4 个字节。
        /// </remarks>        
        Fixed32 = 0b011 << 5,

        /// <summary>
        /// 64 位值。
        /// </summary>
        /// <remarks>        
        /// 后面跟随 8 个字节。
        /// </remarks>        
        Fixed64 = 0b100 << 5,

        /// <summary>
        /// 对先前已编码值的引用。
        /// </summary>
        /// <remarks>        
        /// 后面跟随 8 个字节。
        /// </remarks>        
        Reference = 0b110 << 5, // 后面跟随以 VarInt 编码的引用，指向先前已定义的对象。注意仍必须包含 SchemaType 和类型说明。

        /// <summary>
        /// 控制标记，其余位包含一个 <see cref="ExtendedWireType"/> 值。
        /// </summary>
        Extended = 0b111 << 5, // 这是控制标记。架构类型和内嵌字段 ID 无效，其余 5 位用于控制信息。
    }
}
