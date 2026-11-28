namespace Orleans.Streams.Core
{
    /// <summary>
    /// 提供获取 <see cref="IStreamSubscriptionManager"/> 实例的功能。
    /// </summary>
    public interface IStreamSubscriptionManagerAdmin
    {
        /// <summary>
        /// 获取流订阅管理器。
        /// </summary>
        /// <param name="managerType">管理器的类型。</param>
        /// <returns><see cref="IStreamSubscriptionManager"/>。</returns>
        IStreamSubscriptionManager GetStreamSubscriptionManager(string managerType);
    }

    /// <summary>
    /// <see cref="IStreamSubscriptionManagerAdmin.GetStreamSubscriptionManager(string)"/> 使用的常量。
    /// </summary>
    public static class StreamSubscriptionManagerType
    {
        /// <summary>
        /// 显式订阅管理器。
        /// </summary>
        public const string ExplicitSubscribeOnly = "ExplicitSubscribeOnly";
    }
}
