namespace Orleans.Runtime
{
    /// <summary>
    /// Silo 可能具有的状态。
    /// </summary>
    [GenerateSerializer]
    public enum SiloStatus
    {
        /// <summary>
        /// 状态未知。
        /// </summary>
        None = 0,

        /// <summary>
        /// 此 Silo 刚刚创建，但尚未启动。
        /// </summary>
        Created = 1,

        /// <summary>
        /// 此 Silo 刚刚启动，但尚未就绪，正在尝试加入集群。
        /// </summary>
        Joining = 2,

        /// <summary>
        /// 此 Silo 存活且能够正常工作。
        /// </summary>
        Active = 3,

        /// <summary>
        /// 此 Silo 正在关闭自身。
        /// </summary>
        ShuttingDown = 4,

        /// <summary>
        /// 此 Silo 正在停止自身。
        /// </summary>
        Stopping = 5,

        /// <summary>
        /// 此 Silo 已停用或被视为死亡。
        /// </summary>
        Dead = 6
    }

    /// <summary>
    /// <see cref="SiloStatus"/> 的扩展方法。
    /// </summary>
    public static class SiloStatusExtensions
    {
        /// <summary>
        /// 如果此 Silo 当前正在终止（ShuttingDown、Stopping 或 Dead），则返回 true。
        /// </summary>
        /// <param name="siloStatus">Silo 状态。</param>
        /// <returns><c>true</c> 表示指定 Silo 状态为正在终止；否则为 <c>false</c>。</returns>
        public static bool IsTerminating(this SiloStatus siloStatus)
        {
            return siloStatus == SiloStatus.ShuttingDown || siloStatus == SiloStatus.Stopping || siloStatus == SiloStatus.Dead;
        }
    }
}
