namespace Orleans.Streams.Core
{
    /// <summary>
    /// 提供获取 <see cref="IStreamSubscriptionManager"/> 实例的功能。
    /// </summary>
    public interface IStreamSubscriptionManagerRetriever
    {
        /// <summary>
        /// 接通了水流订阅经理
        /// </summary>
        /// <returns>下 <see cref="IStreamSubscriptionManager"/>,或 <see langword="null"/> 如果流源提供者没有配置的订阅管理器( ) (O)e.g. 仅限注册的不公开订阅提供商)。</returns>
        IStreamSubscriptionManager? GetStreamSubscriptionManager();
    }
}
