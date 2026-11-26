using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 指定可回放的持续流订阅初始位置。
    /// </summary>
    /// <remarks>
    /// 在滚动部署期间,在 silos 主机持续流后使用启动点订阅
    /// 拖拉剂已升级为支持此环的版本。
    /// </remarks>
    public enum StreamSubscriptionStartPosition
    {
        /// <summary>
        /// 开始在订阅建立后发布消息。
        /// </summary>
        Latest,

        /// <summary>
        /// 以本地队列缓存中目前保留的目标流最早消息开始( 包括所有信息) 。
        /// 当缓存没有保留流信息时, 发送将开始于第一个未来消息 。
        /// </summary>
        EarliestAvailable,
    }

    internal static class StreamSubscriptionStartPositionExtensions
    {
        public static void Validate(this StreamSubscriptionStartPosition startPosition)
        {
            if (startPosition is not StreamSubscriptionStartPosition.Latest and not StreamSubscriptionStartPosition.EarliestAvailable)
            {
                throw new ArgumentOutOfRangeException(nameof(startPosition), startPosition, "The subscription start position is not defined.");
            }
        }
    }
}
