namespace Orleans.Runtime
{
    internal interface IActivationCollector
    {
        /// <summary>
        /// 安排回收。
        /// </summary>
        /// <param name="item">要安排回收的激活实例。</param>
        /// <returns></returns>
        void ScheduleCollection(ActivationData item);

        /// <summary>
        /// 尝试重新安排回收。
        /// </summary>
        /// <param name="item">要重新安排回收的激活实例。</param>
        /// <returns></returns>
        bool TryRescheduleCollection(ActivationData item);
    }
}
