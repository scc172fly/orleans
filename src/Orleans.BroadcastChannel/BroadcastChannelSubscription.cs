using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 表示 grain对广播频道的暗中订阅。
    /// </summary>
    public interface IBroadcastChannelSubscription
    {
        /// <summary>
        /// 获取频道标识符。
        /// </summary>
        public ChannelId ChannelId { get; }

        /// <summary>
        /// 获取广播频道供应商的名字。
        /// </summary>
        public string ProviderName { get; }

        /// <summary>
        /// 随员接收此订阅的项目和发送错误的处理器。
        /// </summary>
        /// <typeparam name="T">频道项目类型。</typeparam>
        /// <param name="onPublished">每个已出版的物品都援引处理器。</param>
        /// <param name="onError">当项目交付失败时引用的可选处理器。</param>
        /// <returns>一件在接连者被接连的时候完成的任务。</returns>
        Task Attach<T>(Func<T, Task> onPublished, Func<Exception, Task>? onError = null);

        /// <summary>
        /// 附加频道订阅的取消- 注意回调 。
        /// </summary>
        /// <typeparam name="T">频道元素类型。</typeparam>
        /// <param name="onPublished">元素发布时所引用的回调。</param>
        /// <param name="onError">交付失败时所援引的回调。</param>
        /// <returns>A <see cref="Task"/> 代表操作。</returns>
        Task Attach<T>(
            Func<T, CancellationToken, Task> onPublished,
            Func<Exception, CancellationToken, Task>? onError = null) =>
            Attach<T>(
                item => onPublished(item, CancellationToken.None),
                onError is null ? null : exception => onError(exception, CancellationToken.None));
    }

    /// <summary>
    /// 当启动 grain的隐性广播频道订阅时接收通知。
    /// </summary>
    public interface IOnBroadcastChannelSubscribed
    {
        /// <summary>
        /// 指定操作员订阅已激活的广播频道。
        /// </summary>
        /// <param name="streamSubscription">启动的订阅。</param>
        /// <returns>一项在订阅初始化完成后完成的任务。</returns>
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
