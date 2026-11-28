using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Messaging;

/// <summary>处理其接受的收件箱消息。</summary>
/// <remarks>
/// 收件箱将每条消息分派给首个其 <see cref="CanHandle"/> 返回
/// <see langword="true"/> 的已注册处理程序。选择过程仅接收消息，因此处理程序只有在收件箱选中它后，
/// 才能通过 <see cref="HandleAsync"/> 暂存终态结果。
/// </remarks>
public interface IInboxHandler
{
    /// <summary>确定此处理程序是否处理该消息。</summary>
    /// <param name="message">要路由的消息。</param>
    /// <returns><see langword="true"/> 表示此处理程序处理该消息。</returns>
    bool CanHandle(InboxMessage message);

    /// <summary>处理消息。</summary>
    /// <param name="context">消息及其终态操作。</param>
    /// <param name="cancellationToken">本次尝试的取消令牌。</param>
    /// <returns>处理程序返回时完成的任务。</returns>
    ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken);
}
