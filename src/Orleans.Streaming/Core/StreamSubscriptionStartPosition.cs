using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 指定支持回溯的持久化流订阅的初始位置。
    /// </summary>
    /// <remarks>
    /// 在滚动部署期间，应等待托管持久化流拉取代理的 Silo
    /// 全部升级到支持此枚举的版本后，再使用指定起始位置的订阅。
    /// </remarks>
    public enum StreamSubscriptionStartPosition
    {
        /// <summary>
        /// 从订阅建立后发布的消息开始。
        /// </summary>
        Latest,

        /// <summary>
        /// 从本地队列缓存当前为目标流保留的最早消息开始（包含该消息）。
        /// 如果缓存中没有该流的保留消息，则从该流未来的第一条消息开始投递。
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
