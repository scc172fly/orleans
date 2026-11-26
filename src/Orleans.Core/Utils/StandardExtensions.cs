using System;


namespace Orleans.Internal
{
    /// <summary>
    /// 用户端类包含各种应用实用方法和 grain 代码。
    /// </summary>
    internal static class StandardExtensions
    {
        public static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;

        public static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;
    }
}
