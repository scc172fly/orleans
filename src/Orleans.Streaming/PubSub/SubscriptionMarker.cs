using System;

namespace Orleans.Streams
{
    /// <summary>
    /// a subscriptionId 标为默示订阅代号或明确订阅代号。
    /// 用户类型旗是国际行会中最后一字节的高位。
    /// 1: 默示订阅
    /// 0:明确订阅
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
            // 如果设定了高位的上一个字节返回真实
            return (guidBytes[15] & 0x80) != 0;
        }

        private static Guid MarkSubscriptionGuid(Guid subscriptionGuid, bool isImplicitSubscription)
        {
            Span<byte> guidBytes = stackalloc byte[16];
            subscriptionGuid.TryWriteBytes(guidBytes);
            if (isImplicitSubscription)
            {
                // 设置高位的上一个字节
                guidBytes[15] |= 0x80;
            }
            else
            {
                // 明显高位的上一个字节
                guidBytes[15] &= 0x7f;
            }

            return new Guid(guidBytes);
        }
    }
}
