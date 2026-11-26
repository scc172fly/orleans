namespace Orleans.Runtime
{
    /// <summary>
    /// 可能状况的 silo.
    /// </summary>
    [GenerateSerializer]
    public enum SiloStatus
    {
        /// <summary>
        /// 未知状态 。
        /// </summary>
        None = 0,

        /// <summary>
        /// 此 silo 刚刚创建, 但还没有启动 。
        /// </summary>
        Created = 1,

        /// <summary>
        /// 今 silo 刚刚开始,但还没有准备好。它正试图加入这一组。
        /// </summary>
        Joining = 2,

        /// <summary>
        /// 这个 silo 是活的,功能正常。
        /// </summary>
        Active = 3,

        /// <summary>
        /// 今 silo 自己关闭了
        /// </summary>
        ShuttingDown = 4,

        /// <summary>
        /// 今 silo 自己也停止了下来
        /// </summary>
        Stopping = 5,

        /// <summary>
        /// 这 silo 已被解除了作用/被认为已死亡。
        /// </summary>
        Dead = 6
    }

    /// <summary>
    /// <see cref="SiloStatus"/>的扩展期。
    /// </summary>
    public static class SiloStatusExtensions
    {
        /// <summary>
        /// 如果此 silo 正在终止 ShuttingDown、 停止或死亡, 请返回真实的 。
        /// </summary>
        /// <param name="siloStatus"> silo 状态。</param>
        /// <returns><c>true</c> 如果指定 silo 状态终止;否则, <c>false</c>.</returns>
        public static bool IsTerminating(this SiloStatus siloStatus)
        {
            return siloStatus == SiloStatus.ShuttingDown || siloStatus == SiloStatus.Stopping || siloStatus == SiloStatus.Dead;
        }
    }
}
