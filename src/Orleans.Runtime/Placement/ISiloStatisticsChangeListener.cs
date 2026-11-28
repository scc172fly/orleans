namespace Orleans.Runtime
{
    internal interface ISiloStatisticsChangeListener
    {
        /// <summary>
        /// 在新的统计数据到达时接收通知。
        /// </summary>
        /// <param name="updatedSilo">已更新的 Silo。</param>
        /// <param name="newStats">新的 Silo 统计信息。</param>
        void SiloStatisticsChangeNotification(SiloAddress updatedSilo, SiloRuntimeStatistics newStats);

        void RemoveSilo(SiloAddress removedSilo);
    }
}
