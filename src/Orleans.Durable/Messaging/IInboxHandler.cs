using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Messaging;

/// <summary>处理它接受的收件箱信件。</summary>
/// <remarks>
/// 收件箱将每条消息发送到第一个已注册的处理器, 该处理器返回 <see cref="CanHandle"/>
/// <see langword="true"/>。选择只接收电文,因此处理者只能将终端结果进行处理
/// 从 <see cref="HandleAsync"/>中,在收件箱选择后。
/// </remarks>
public interface IInboxHandler
{
    /// <summary>确定该处理器是否处理信件。</summary>
    /// <param name="message">电文到路由。</param>
    /// <returns><see langword="true"/> 当该处理器处理信件时。</returns>
    bool CanHandle(InboxMessage message);

    /// <summary>处理信件。</summary>
    /// <param name="context">信息及其终端操作</param>
    /// <param name="cancellationToken">尝试符号。</param>
    /// <returns>任务, 处理返回时完成该任务。</returns>
    ValueTask HandleAsync(IInboxHandlerContext context, CancellationToken cancellationToken);
}
