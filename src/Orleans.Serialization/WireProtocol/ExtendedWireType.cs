namespace Orleans.Serialization.WireProtocol
{
    /// <summary>
    /// 代表长线型
    /// </summary>
    public enum ExtendedWireType : uint
    {
        /// <summary>
        /// 标记标签限制字段的结尾。
        /// </summary>
        EndTagDelimited = 0b00 << 3,

        /// <summary>
        /// 标记标签限制对象中基型字段的结束。
        /// </summary>
        EndBaseFields = 0b01 << 3,
    }
}
