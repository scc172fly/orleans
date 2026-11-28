using System;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Common;

internal static class EventSequenceTokenCompatibility
{
    public static bool IsCompatibleNumericToken(StreamSequenceToken left, StreamSequenceToken right)
        => left.GetType() == right.GetType()
            || TryGetCompatibilityDomain(left, out var leftDomain)
                && TryGetCompatibilityDomain(right, out var rightDomain)
                && leftDomain == rightDomain;

    public static int Compare(StreamSequenceToken left, StreamSequenceToken right)
    {
        Normalize(ref left, ref right);
        return left.CompareTo(right);
    }

    public static bool AreEqual(StreamSequenceToken left, StreamSequenceToken right)
    {
        Normalize(ref left, ref right);
        return left.Equals(right);
    }

    private static bool TryGetCompatibilityDomain(StreamSequenceToken token, out Type domain)
    {
        domain = token switch
        {
            EventSequenceToken eventToken => eventToken.GetSequenceTokenCompatibilityDomain(),
            EventSequenceTokenV2 eventToken => eventToken.GetSequenceTokenCompatibilityDomain(),
            _ => null!,
        };

        return domain is not null;
    }

    private static void Normalize(ref StreamSequenceToken left, ref StreamSequenceToken right)
    {
        // 仅在恢复比较中对提供程序特定的旧版位置进行规范化。
        // 公开的令牌相等性保持提供程序的契约，已投递令牌保留其元数据。
        if (left is EventSequenceToken leftProvider)
        {
            right = leftProvider.NormalizeLegacyToken(right);
        }

        if (right is EventSequenceToken rightProvider)
        {
            left = rightProvider.NormalizeLegacyToken(left);
        }
    }
}
