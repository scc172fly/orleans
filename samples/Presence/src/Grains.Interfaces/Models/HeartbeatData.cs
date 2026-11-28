using System.Text;

namespace Presence.Grains.Models;

/// <summary>
/// 游戏会话的心跳数据。
/// 此类不可变。
/// 对此类的操作始终返回新的副本。
/// </summary>
[GenerateSerializer]
public record class HeartbeatData(
    Guid GameKey,
    GameStatus Status)
{
    /// <summary>
    /// 创建包含指定分数的当前心跳数据的不可变副本。
    /// </summary>
    public HeartbeatData WithNewScore(string newScore) =>
        this with { Status = Status.WithNewScore(newScore) };

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append($"Heartbeat:Game={GameKey}");
        builder.AppendJoin(',', Status.PlayerKeys.Select((item, idx) => $"Player{idx + 1}={item}"));
        builder.Append($",Score={Status.Score}");
        return builder.ToString();
   }
}
