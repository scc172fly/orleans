namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 指定通过 <see cref="ITransactionalState{TState}"/>执行的读数的孤立。
    /// </summary>
    public enum ReadIsolation
    {
        /// <summary>
        /// 在共享锁下读取环境交易内部。这是默认的 。
        /// </summary>
        Serializable = 0,

        /// <summary>
        /// 在空环境交易背景下独立阅读本地承诺状态的最新信息
        /// 复制结果,而待审作家保留着他们的锁和暂定状态。
        /// 远程协调写作在参与者收到并坚持后在当地可见
        /// 承诺确认, 可在写入交易完成后发生。
        /// </summary>
        ReadCommitted,
    }
}
