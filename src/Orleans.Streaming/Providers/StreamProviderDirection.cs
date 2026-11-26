namespace Orleans.Streams
{
    /// <summary>
    /// 指定流数据提供者是只读、读写还是只写。
    /// </summary>
    public enum StreamProviderDirection
    {
        /// <summary>
        /// 无。
        /// </summary>
        None,

        /// <summary>
        /// 此提供者可以接收信件, 但无法发送 。
        /// </summary>
        ReadOnly,

        /// <summary>
        /// 此提供者可以发送信件但无法接收 。
        /// </summary>
        WriteOnly,

        /// <summary>
        /// 该提供者既可以发送也可以接收信息。
        /// </summary>
        ReadWrite
    }
}
