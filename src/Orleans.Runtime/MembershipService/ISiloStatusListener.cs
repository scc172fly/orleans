namespace Orleans.Runtime
{
    /// <summary>
    /// 供监听 Silo 状态变更通知的类型实现的接口。
    /// </summary>
    /// <remarks>
    /// 由 Silo 内部关心 ISiloStatusOracle 发出的 Silo 状态通知的各运行时组件实现。
    /// </remarks>
    public interface ISiloStatusListener
    {
        /// <summary>
        /// 接收 Silo 状态事件的通知。
        /// </summary>
        /// <param name="updatedSilo">状态发生更新的 Silo。</param>
        /// <param name="status">Silo 的状态。</param>
        void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status);
    }
}
