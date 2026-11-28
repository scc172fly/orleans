namespace TicTacToe.Grains;

public class PlayerGrain : Grain, IPlayerGrain
{
    private List<Guid> _activeGames = new();
    private List<Guid> _pastGames = new();

    private int _wins;
    private int _loses;
    private int _gamesStarted;

    private string _username = null!;

    public override Task OnActivateAsync(CancellationToken token)
    {
        _activeGames = new List<Guid>();
        _pastGames = new List<Guid>();

        _wins = 0;
        _loses = 0;
        _gamesStarted = 0;

        return base.OnActivateAsync(token);
    }

    public async Task<PairingSummary[]> GetAvailableGames()
    {
        var grain = GrainFactory.GetGrain<IPairingGrain>(0);
        return (await grain.GetGames()).Where(x => !_activeGames.Contains(x.GameId)).ToArray();
    }

    // 创建新游戏，并让当前玩家加入。
    public async Task<Guid> CreateGame()
    {
        _gamesStarted++;

        var gameId = Guid.NewGuid();
        var gameGrain = GrainFactory.GetGrain<IGameGrain>(gameId);  // 创建新游戏。

        // 将当前玩家加入游戏。
        var playerId = this.GetPrimaryKey();  // 当前玩家的 ID。
        await gameGrain.AddPlayerToGame(playerId);
        _activeGames.Add(gameId);
        var name = $"{_username}'s {AddOrdinalSuffix(_gamesStarted.ToString())} game";
        await gameGrain.SetName(name);

        var pairingGrain = GrainFactory.GetGrain<IPairingGrain>(0);
        await pairingGrain.AddGame(gameId, name);

        return gameId;
    }

    // 加入正在等待玩家的游戏。
    public async Task<GameState> JoinGame(Guid gameId)
    {
        var gameGrain = GrainFactory.GetGrain<IGameGrain>(gameId);

        var state = await gameGrain.AddPlayerToGame(this.GetPrimaryKey());
        _activeGames.Add(gameId);

        var pairingGrain = GrainFactory.GetGrain<IPairingGrain>(0);
        await pairingGrain.RemoveGame(gameId);

        return state;
    }

    // 游戏结束时离开游戏。
    public Task LeaveGame(Guid gameId, GameOutcome outcome)
    {
        // 管理游戏列表。
        _activeGames.Remove(gameId);
        _pastGames.Add(gameId);

        // 管理累计统计值。
        _ = outcome switch
        {
            GameOutcome.Win => _wins++,
            GameOutcome.Lose => _loses++,
            _ => 0
        };

        return Task.CompletedTask;
    }

    public async Task<List<GameSummary>> GetGameSummaries()
    {
        var tasks = new List<Task<GameSummary>>();
        foreach (var gameId in _activeGames)
        {
            var game = GrainFactory.GetGrain<IGameGrain>(gameId);
            tasks.Add(game.GetSummary(this.GetPrimaryKey()));
        }

        await Task.WhenAll(tasks);
        return tasks.Select(x => x.Result).ToList();
    }

    public Task SetUsername(string name)
    {
        _username = name;
        return Task.CompletedTask;
    }

    public Task<string> GetUsername() => Task.FromResult(_username);

    private static string AddOrdinalSuffix(string number)
    {
        var n = int.Parse(number);
        var nMod100 = n % 100;

        return nMod100 switch
        {
            >= 11 and <= 13 => string.Concat(number, "th"),
            _ => (n % 10) switch
            {
                1 => string.Concat(number, "st"),
                2 => string.Concat(number, "nd"),
                3 => string.Concat(number, "rd"),
                _ => string.Concat(number, "th"),
            }
        };
    }
}
