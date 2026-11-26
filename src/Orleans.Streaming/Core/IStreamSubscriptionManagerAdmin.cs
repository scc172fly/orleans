namespace Orleans.Streams.Core
{
    /// <summary>
    /// 检索 <see cref="IStreamSubscriptionManager"/> 实例的功能性。
    /// </summary>
    public interface IStreamSubscriptionManagerAdmin
    {
        /// <summary>
        /// 接通了水流订阅经理
        /// </summary>
        /// <param name="managerType">经理的类型。</param>
        /// <returns>下 <see cref="IStreamSubscriptionManager"/>.</returns>
        IStreamSubscriptionManager GetStreamSubscriptionManager(string managerType);
    }

    /// <summary>
    /// <see cref="IStreamSubscriptionManagerAdmin.GetStreamSubscriptionManager(string)"/>的常数。
    /// </summary>
    public static class StreamSubscriptionManagerType
    {
        /// <summary>
        /// 明确的订阅经理。
        /// </summary>
        public const string ExplicitSubscribeOnly = "ExplicitSubscribeOnly";
    }
}
