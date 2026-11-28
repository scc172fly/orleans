namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 指定通过 <see cref="ITransactionalState{TState}"/> 执行读取时使用的隔离级别。
    /// </summary>
    public enum ReadIsolation
    {
        /// <summary>
        /// 在环境事务中持有共享锁进行读取。这是默认行为。
        /// </summary>
        Serializable = 0,

        /// <summary>
        /// 在没有环境事务的上下文中读取本地最新已提交的状态，并独立
        /// 复制结果；待定的写入方仍保留各自的锁和暂定状态。
        /// 远程协调的写入在参与者收到并持久化
        /// 提交确认后才在本地可见，这可能发生在写入事务完成之后。
        /// </summary>
        ReadCommitted,
    }
}
