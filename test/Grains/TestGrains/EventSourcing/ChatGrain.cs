using Orleans.EventSourcing;
using Orleans.Providers;
using System.Xml.Linq;
using TestGrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 实现聊天功能的日志化 Grain 示例。
    /// Grain 的状态为 XML 文档（System.Xml.Linq.XDocument）。
    ///
    /// 配置为使用默认存储提供程序。
    /// 配置为使用 LogStorage 一致性提供程序。
    ///
    /// 这意味着我们持久化所有事件；由于 Grain 加载时会重放事件，
    /// 以后仍可以更改 XML 架构
    ///
    /// </summary>

    [StorageProvider(ProviderName = "Default")]
    [LogConsistencyProvider(ProviderName = "LogStorage")]
    public class ChatGrain : JournaledGrain<XDocument, IChatEvent>, IChatGrain
    {
        // 确保聊天室在首次使用时正确初始化
        // 因此重写默认激活逻辑，在需要时插入创建事件
        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // 首先，等待从存储加载所有事件，使当前版本追上最新版本
            await base.OnActivateAsync(cancellationToken);

            // 如果聊天室尚未初始化，则现在进行初始化
            if (Version == 0)
            {
                // 这里使用条件事件（以防创建操作与其他集群发生竞争）
                // （只有版本在此期间尚未改变时，条件事件才会提交）
                await RaiseConditionalEvent(new CreatedEvent()
                {
                    Timestamp = DateTime.UtcNow,
                    Origin = typeof(ChatGrain).FullName!
                });
            }
        }

        /// <summary>
        /// 为了将事件正确应用于 Grain 状态，
        /// 必须重写状态转换函数
        /// （因为 XDocument 对象没有 Apply 函数）
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
