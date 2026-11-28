namespace Orleans.Streams
{
    /// <summary>
    /// 标识流提供程序是只读、读写还是只写。
    /// </summary>
    public enum StreamProviderDirection
    {
        /// <summary>
        /// 无。
        /// </summary>
        None,

        /// <summary>
        /// 此提供程序可以接收消息，但不能发送消息。
        /// </summary>
        ReadOnly,

        /// <summary>
        /// 此提供程序可以发送消息，但不能接收消息。
        /// </summary>
        WriteOnly,

        /// <summary>
        /// 此提供程序既可以发送也可以接收消息。
        /// </summary>
        ReadWrite
    }
}
