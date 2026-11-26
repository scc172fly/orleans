namespace Orleans.Runtime
{
    /// <summary>
    /// 监听状态变更通知 silo 的类型的接口。
    /// </summary>
    /// <remarks>
    /// 由不同部门执行silo 有兴趣运行的运行时组件 silo 通知的状态状况通知 ISiloStatusOracle.
    /// </remarks>
    public interface ISiloStatusListener
    {
        /// <summary>
        /// 接收关于 silo 状态事件的通知。 
        /// </summary>
        /// <param name="updatedSilo">A silo 以更新。</param>
        /// <param name="status">地位状况的一国或 silo.</param>
        void SiloStatusChangeNotification(SiloAddress updatedSilo, SiloStatus status);
    }
}
