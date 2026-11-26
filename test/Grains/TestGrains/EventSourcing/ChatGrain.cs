using Orleans.EventSourcing;
using Orleans.Providers;
using System.Xml.Linq;
using TestGrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 例如,一个日记 grain 进行聊天的例子。
    /// grain 状态为 XML 文档 (System.Xml.Linq.XDocument)。
    ///
    /// 配置后使用默认的存储提供者。
    /// 配置后使用 LogStorage 一致性提供商。
    ///
    /// 这意味着我们坚持所有事件; 因为当装入一个 grain 时, 事件会被重放
    /// 以后再换XML的图案
    ///
    /// </summary>

    [StorageProvider(ProviderName = "Default")]
    [LogConsistencyProvider(ProviderName = "LogStorage")]
    public class ChatGrain : JournaledGrain<XDocument, IChatEvent>, IChatGrain
    {
        // 我们希望确保聊天在首次使用时正确初始化
        // 以在需要时插入创建事件
        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // 首先,等待所有事件从存储中装入, 所以我们会赶上最新的版本
            await base.OnActivateAsync(cancellationToken);

            // 如果聊天尚未初始化, 请现在就开始
            if (Version == 0)
            {
                // 我们使用有条件的事件(如果创建与其他集群赛跑)
                // (有条件的事件只有在版本尚未改变的情况下才会发生)
                await RaiseConditionalEvent(new CreatedEvent()
                {
                    Timestamp = DateTime.UtcNow,
                    Origin = typeof(ChatGrain).FullName!
                });
            }
        }

        /// <summary>
        /// 为了正确应用事件到 grain 状态,我们
        /// 必须超越过渡函数
        /// (因为 XDocument 对象没有应用功能)
        /// </summary>
        protected override void TransitionState(XDocument state, IChatEvent @event)
        {
            @event.Update(state);
        }

        public Task<XDocument> GetChat()
        {
            return Task.FromResult(TentativeState);
        }

        public Task Post(Guid guid, string user, string text)
        {
            RaiseEvent(new PostedEvent() { Guid = guid, User = user, Text = text, Timestamp = DateTime.UtcNow });
            return Task.CompletedTask;
        }

        public Task Delete(Guid guid)
        {
            RaiseEvent(new DeletedEvent() { Guid = guid });
            return Task.CompletedTask;
        }

        public Task Edit(Guid guid, string text)
        {
            RaiseEvent(new EditedEvent() { Guid = guid, Text = text });
            return Task.CompletedTask;
        }
    }
}
