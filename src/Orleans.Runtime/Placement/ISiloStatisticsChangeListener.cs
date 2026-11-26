namespace Orleans.Runtime
{
    internal interface ISiloStatisticsChangeListener
    {
        /// <summary>
        /// 新的统计数据到达时接收通知。
        /// </summary>
        /// <param name="updatedSilo">更新 silo。</param>
        /// <param name="newStats">新建新新 Silo 统计。</param>
        void SiloStatisticsChangeNotification(SiloAddress updatedSilo, SiloRuntimeStatistics newStats);

        void RemoveSilo(SiloAddress removedSilo);
    }
}
