namespace TicTacToe.Grains;

public interface IPlayerGrain : IGrainWithGuidKey
{
    // 获取所有活动游戏列表

    Task<PairingSummary[]> GetAvailableGames();

    Task<List<GameSummary>> GetGameSummaries();

    // 创建一个新游戏并加入它
    Task<Guid> CreateGame();

    // 加入一个已有游戏
    Task<GameState> JoinGame(Guid gameId);

    Task LeaveGame(Guid gameId, GameOutcome outcome);

    Task SetUsername(string username);

    Task<string> GetUsername();
}
