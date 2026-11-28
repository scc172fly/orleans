using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 表示 Grain 对广播通道的隐式订阅。
    /// </summary>
    public interface IBroadcastChannelSubscription
    {
        /// <summary>
        /// 获取通道标识符。
        /// </summary>
        public ChannelId ChannelId { get; }

        /// <summary>
        /// 获取广播通道提供程序的名称。
        /// </summary>
        public string ProviderName { get; }

        /// <summary>
        /// 附加用于接收此订阅的元素和投递错误的处理器。
        /// </summary>
        /// <typeparam name="T">通道元素类型。</typeparam>
        /// <param name="onPublished">每次发布元素时调用的处理器。</param>
        /// <param name="onError">元素投递失败时调用的可选处理器。</param>
        /// <returns>在处理器附加完成时完成的任务。</returns>
        Task Attach<T>(Func<T, Task> onPublished, Func<Exception, Task>? onError = null);

        /// <summary>
        /// 为通道订阅附加支持取消的回调。
        /// </summary>
        /// <typeparam name="T">通道元素类型。</typeparam>
        /// <param name="onPublished">元素发布时调用的回调。</param>
        /// <param name="onError">投递失败时调用的回调。</param>
        /// <returns>表示该操作的 <see cref="Task"/>。</returns>
        Task Attach<T>(
            Func<T, CancellationToken, Task> onPublished,
            Func<Exception, CancellationToken, Task>? onError = null) =>
            Attach<T>(
                item => onPublished(item, CancellationToken.None),
                onError is null ? null : exception => onError(exception, CancellationToken.None));
    }

    /// <summary>
    /// 接收 Grain 的隐式广播通道订阅被激活时的通知。
    /// </summary>
    public interface IOnBroadcastChannelSubscribed
    {
        /// <summary>
        /// 为已激活的广播通道订阅附加处理器。
        /// </summary>
        /// <param name="streamSubscription">已激活的订阅。</param>
        /// <returns>在订阅初始化完成时完成的任务。</returns>
        public Task OnSubscribed(IBroadcastChannelSubscription streamSubscription);
    }

    internal class BroadcastChannelSubscription : IBroadcastChannelSubscription
    {
        private readonly BroadcastChannelConsumerExtension _consumerExtension;
        private readonly InternalChannelId _streamId;

        public ChannelId ChannelId => _streamId.ChannelId;

        public string ProviderName => _streamId.ProviderName;

        public BroadcastChannelSubscription(BroadcastChannelConsumerExtension consumerExtension, InternalChannelId streamId)
        {
            _consumerExtension = consumerExtension;
            _streamId = streamId;
        }

        public Task Attach<T>(Func<T, Task> onPublished, Func<Exception, Task>? onError = null)
        {
            _consumerExtension.Attach(_streamId, onPublished, onError);
            return Task.CompletedTask;
        }

        public Task Attach<T>(
            Func<T, CancellationToken, Task> onPublished,
            Func<Exception, CancellationToken, Task>? onError = null)
        {
            _consumerExtension.Attach(_streamId, onPublished, onError);
            return Task.CompletedTask;
        }
    }
}
