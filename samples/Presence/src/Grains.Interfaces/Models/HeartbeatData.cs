using System.Text;

namespace Presence.Grains.Models;

/// <summary>
/// 游戏会话的心跳数据 。
/// 这门课是不可改变的
/// 此类操作总是返回新副本 。
/// </summary>
[GenerateSerializer]
public record class HeartbeatData(
    Guid GameKey,
    GameStatus Status)
{
    /// <summary>
    /// 创建当前心跳的不可变副本, 并使用给定分 。
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
