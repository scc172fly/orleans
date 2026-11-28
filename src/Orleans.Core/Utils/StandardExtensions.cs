using System;


namespace Orleans.Internal
{
    /// <summary>
    /// Utils 类包含应用程序代码和 Grain 代码使用的各种实用方法。
    /// </summary>
    internal static class StandardExtensions
    {
        public static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;

        public static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;
    }
}
