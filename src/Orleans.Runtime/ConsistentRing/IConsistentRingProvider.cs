namespace Orleans.Runtime.ConsistentRing
{
    // 有一天, 这将是唯一的环的提供者, i.e。 目录服务将会使用此

    internal interface IConsistentRingProvider
    {
        /// <summary>
        /// 获取当前 silo的责任范围
        /// </summary>
        /// <returns></returns>
        IRingRange GetMyRange();

        // 以下两个与 ISiloStatusOracle 接口类似.,这取代了 "OnRangeChanged" ,因为 OnRangeChanged 只支持一个订阅

        /// <summary>
        /// 订阅接收更改范围通知
        /// </summary>
        /// <param name="observer">一个接收范围变更通知的观察员界面。</param>
        /// <returns>上下标值,表明订阅成功与否。</returns>
        bool SubscribeToRangeChangeEvents(IRingRangeListener observer);

        /// <summary>
        /// 从接收更改范围通知中取消订阅
        /// </summary>
        /// <param name="observer">一个接收范围变更通知的观察员界面。</param>
        /// <returns>布尔值,表明未订阅是否成功</returns>
        bool UnSubscribeFromRangeChangeEvents(IRingRangeListener observer);

        /// <summary>
        /// 获取对 silo 负责的 <paramref name="key"/> , 并按一致的散列
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        SiloAddress? GetPrimaryTargetSilo(uint key);
    }

    // 类似 ISiloStatusListener
    internal interface IRingRangeListener
    {
        void RangeChangeNotification(IRingRange old, IRingRange now, bool increased);
    }
}
