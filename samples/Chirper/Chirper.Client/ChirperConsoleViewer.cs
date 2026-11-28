using Chirper.Grains;
using Chirper.Grains.Models;
using Spectre.Console;

namespace Chirper.Client;

/// <summary>
/// 实现 <see cref="IChirperViewer"/>，将通知输出到控制台。
/// </summary>
public sealed class ChirperConsoleViewer : IChirperViewer
{
    /// <summary>
    /// 正在观察的账户的用户名。
    /// </summary>
    private readonly string _userName;

    /// <summary>
    /// 创建新的 <see cref="IChirperViewer"/>，将通知输出到控制台。
    /// </summary>
    /// <param name="userName">正在观察的账户的用户名。</param>
    public ChirperConsoleViewer(string userName) =>
        _userName = userName ?? throw new ArgumentNullException(nameof(userName));

    /// <inheritdoc />
    public void NewChirp(ChirperMessage message) =>
        AnsiConsole.MarkupLine(
            "[[[dim]{0}[/]]] [aqua]{1}[/] [bold yellow]chirped:[/] {2}",
            message.Timestamp.LocalDateTime, message.PublisherUserName, message.Message);

    public void NewFollower(string username) =>
        AnsiConsole.MarkupLine(
            "[bold grey][[[/][bold yellow]![/][bold grey]]][/] [aqua]{0}[/] is now following [navy]{1}[/]",
            username, _userName);

    /// <inheritdoc />
    public void SubscriptionAdded(string username) =>
        AnsiConsole.MarkupLine(
            "[bold grey][[[/][bold lime]✓[/][bold grey]]][/] [navy]{0}[/] is now following [aqua]{1}[/]",
            _userName, username);

    /// <inheritdoc />
    public void SubscriptionRemoved(string username) =>
        AnsiConsole.MarkupLine(
            "[bold grey][[[/][bold lime]✓[/][bold grey]]][/] [navy]{0}[/] is no longer following [aqua]{1}[/]",
            _userName, username);
}
