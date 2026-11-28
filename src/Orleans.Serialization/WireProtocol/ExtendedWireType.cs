namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 表示扩展传输格式类型。
    /// </summary>
    public enum ExtendedWireType : uint
    {
        /// <summary>
        /// 标记由标记分隔的字段的结束位置。
        /// </summary>
        EndTagDelimited = 0b00 << 3,

        /// <summary>
        /// 标记由标记分隔的对象中基类型字段的结束位置。
        /// </summary>
        EndBaseFields = 0b01 << 3,
    }
}
