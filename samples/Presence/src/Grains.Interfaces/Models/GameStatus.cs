using System.Collections.Immutable;

namespace Presence.Grains.Models;

/// <summary>
/// 游戏当前状态的数据。
/// 此类不可变。
/// 对此类的操作始终返回新的副本。
/// </summary>
[GenerateSerializer]
public record class GameStatus(
    ImmutableHashSet<Guid> PlayerKeys,
    string Score)
{
    /// <summary>
    /// 创建包含指定分数的当前游戏状态的不可变副本。
    /// </summary>
    public GameStatus WithNewScore(string newScore) =>
        this with { Score = newScore };

    public static GameStatus Empty { get; } =
        new GameStatus(
            ImmutableHashSet<Guid>.Empty,
            string.Empty);
}
