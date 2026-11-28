namespace Orleans.Streams.Core
{
    /// <summary>
    /// 提供获取 <see cref="IStreamSubscriptionManager"/> 实例的功能。
    /// </summary>
    public interface IStreamSubscriptionManagerRetriever
    {
        /// <summary>
        /// 获取流订阅管理器。
        /// </summary>
        /// <returns><see cref="IStreamSubscriptionManager"/>；如果流提供程序未配置订阅管理器（例如仅支持隐式订阅的提供程序），则为 <see langword="null"/>。</returns>
        IStreamSubscriptionManager? GetStreamSubscriptionManager();
    }
}
