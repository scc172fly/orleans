using System.Collections.Immutable;

namespace Presence.Grains.Models;

/// <summary>
/// 游戏当前状态的数据。
/// 这门课是不可改变的
/// 此类操作总是返回新副本 。
/// </summary>
[GenerateSerializer]
public record class GameStatus(
    ImmutableHashSet<Guid> PlayerKeys,
    string Score)
{
    /// <summary>
    /// 创建当前游戏状态的不可更改副本, 并使用给定分 。
    /// </summary>
    public GameStatus WithNewScore(string newScore) =>
        this with { Score = newScore };

    public static GameStatus Empty { get; } =
        new GameStatus(
            ImmutableHashSet<Guid>.Empty,
            string.Empty);
}
