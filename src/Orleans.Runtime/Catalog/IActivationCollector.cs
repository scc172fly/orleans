namespace Orleans.Runtime
{
    internal interface IActivationCollector
    {
        /// <summary>
        /// 收集调度表。
        /// </summary>
        /// <param name="item">启动计划将排定。</param>
        /// <returns></returns>
        void ScheduleCollection(ActivationData item);

        /// <summary>
        /// 试图重新安排收藏时间 。
        /// </summary>
        /// <param name="item">激活将重新安排。</param>
        /// <returns></returns>
        bool TryRescheduleCollection(ActivationData item);
    }
}
