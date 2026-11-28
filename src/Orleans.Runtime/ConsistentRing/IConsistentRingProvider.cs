namespace Orleans.Runtime.ConsistentRing
{
    // 将来这将成为环的唯一提供程序，即目录服务也将使用它。

    internal interface IConsistentRingProvider
    {
        /// <summary>
        /// 获取当前 Silo 的责任范围。
        /// </summary>
        /// <returns></returns>
        IRingRange GetMyRange();

        // 以下两个方法类似 ISiloStatusOracle 接口，用于替代 OnRangeChanged，因为后者只支持一个订阅。

        /// <summary>
        /// 订阅范围变化通知。
        /// </summary>
        /// <param name="observer">用于接收范围变化通知的观察者接口。</param>
        /// <returns>指示订阅是否成功的布尔值。</returns>
        bool SubscribeToRangeChangeEvents(IRingRangeListener observer);

        /// <summary>
        /// 取消范围变化通知的订阅。
        /// </summary>
        /// <param name="observer">用于接收范围变化通知的观察者接口。</param>
        /// <returns>指示取消订阅是否成功的布尔值。</returns>
        bool UnSubscribeFromRangeChangeEvents(IRingRangeListener observer);

        /// <summary>
        /// 根据一致性哈希获取负责 <paramref name="key"/> 的 Silo。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        SiloAddress? GetPrimaryTargetSilo(uint key);
    }

    // 类似 ISiloStatusListener。
    internal interface IRingRangeListener
    {
        void RangeChangeNotification(IRingRange old, IRingRange now, bool increased);
    }
}
