using System;

namespace Orleans.Streams
{
    /// <summary>
    /// 将 subscriptionId 标记为隐式订阅 ID 或显式订阅 ID。
    /// Guid 最后一个字节的最高位是订阅类型标志。
    /// 1：隐式订阅。
    /// 0：显式订阅。
    /// </summary>
    internal static class SubscriptionMarker
    {
        internal static Guid MarkAsExplicitSubscriptionId(Guid subscriptionGuid)
        {
            return MarkSubscriptionGuid(subscriptionGuid, false);
        }

        internal static Guid MarkAsImplictSubscriptionId(Guid subscriptionGuid)
        {
            return MarkSubscriptionGuid(subscriptionGuid, true);
        }

        internal static bool IsImplicitSubscription(Guid subscriptionGuid)
        {
            Span<byte> guidBytes = stackalloc byte[16];
            subscriptionGuid.TryWriteBytes(guidBytes);
            // 如果最后一个字节的最高位已设置，则返回 true。
            return (guidBytes[15] & 0x80) != 0;
        }

        private static Guid MarkSubscriptionGuid(Guid subscriptionGuid, bool isImplicitSubscription)
        {
            Span<byte> guidBytes = stackalloc byte[16];
            subscriptionGuid.TryWriteBytes(guidBytes);
            if (isImplicitSubscription)
            {
                // 设置最后一个字节的最高位。
                guidBytes[15] |= 0x80;
            }
            else
            {
                // 清除最后一个字节的最高位。
                guidBytes[15] &= 0x7f;
            }

            return new Guid(guidBytes);
        }
    }
}
