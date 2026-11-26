namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 指定字段的运行时间类型(系统类型)。
    /// </summary>
    public enum SchemaType : uint
    {
        /// <summary>
        /// 表示运行时间类型是当前系统图所预期的准确类型。
        /// </summary>
        Expected = 0b00 << 3,

        /// <summary>
        /// 表示运行时间类型是已知类型的实例。随后是 VarInt 类型代号。
        /// </summary>
        WellKnown = 0b01 << 3,

        /// <summary>
        /// 表示运行时间类型以命名类型编码。随后是编码类型名称。
        /// </summary>
        Encoded = 0b10 << 3,

        /// <summary>
        /// 表示运行时间类型是先前指定的类型。随后有一个 VarInt ,标明了正在重新使用哪种前一种类型。
        /// </summary>
        Referenced = 0b11 << 3,
    }
}
