namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 标识字段的运行时类型（架构类型）。
    /// </summary>
    public enum SchemaType : uint
    {
        /// <summary>
        /// 表示运行时类型与当前架构预期的类型完全一致。
        /// </summary>
        Expected = 0b00 << 3,

        /// <summary>
        /// 表示运行时类型属于已知类型，后面跟随一个以 VarInt 编码的类型 ID。
        /// </summary>
        WellKnown = 0b01 << 3,

        /// <summary>
        /// 表示运行时类型以命名类型编码，后面跟随编码后的类型名称。
        /// </summary>
        Encoded = 0b10 << 3,

        /// <summary>
        /// 表示运行时类型是先前已指定的类型，后面跟随一个 VarInt，指示复用哪个先前的类型。
        /// </summary>
        Referenced = 0b11 << 3,
    }
}
