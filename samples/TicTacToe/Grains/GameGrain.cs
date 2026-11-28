using Orleans.Concurrency;

namespace TicTacToe.Grains;

/// <summary>
/// Orleans Grain 实现类 GameGrain。
/// </summary>
[Reentrant]
public class GameGrain : Grain, IGameGrain
{
    // 当前游戏的玩家列表。
    // 为简单起见，玩家 0 始终使用 "O"，玩家 1 使用 "X"。
    // 游戏开始时随机决定哪位玩家先走，并通过 indexNextPlayerToMove 设置。
    public List<Guid> _playerIds = new();
    private int _indexNextPlayerToMove;

    private GameState _gameState;
    private Guid _winnerId;
    private Guid _loserId;

    // 通过记录游戏中的每一步落子来保存游戏，以便重建落子顺序。
    // 在游戏进行期间，还使用二维数组表示棋盘，以便
    // 检查合法落子、获胜连线等。
    // -1 表示空格，0 和 1 表示玩家索引。
    private List<GameMove> _moves = new();
    private int[,] _board = null!;

    private string _name = null!;

    // 初始化。
    public override Task OnActivateAsync(CancellationToken token)
    {
        // 确保新建游戏处于正确状态。
        _playerIds = new List<Guid>();
        _moves = new List<GameMove>();
        _indexNextPlayerToMove = -1;  // 安全默认值——游戏开始时设置为 0 或 1。
        _board = new int[3, 3] { { -1, -1, -1 }, { -1, -1, -1 }, { -1, -1, -1 } };  // -1 表示空格。

        _gameState = GameState.AwaitingPlayers;
        _winnerId = Guid.Empty;
        _loserId = Guid.Empty;

        return base.OnActivateAsync(token);
    }

    // 向游戏添加玩家。
    public Task<GameState> AddPlayerToGame(Guid player)
    {
        // 检查是否允许加入此游戏。
        if (_gameState is GameState.Finished) throw new ApplicationException("Can't join game once its over");
        if (_gameState is GameState.InPlay) throw new ApplicationException("Can't join game once its in play");

        // 添加玩家。
        _playerIds.Add(player);

        // 检查游戏是否已准备好开始。
        if (_gameState is GameState.AwaitingPlayers && _playerIds.Count is 2)
        {
            // 一场新游戏即将开始。
            _gameState = GameState.InPlay;
            _indexNextPlayerToMove = Random.Shared.Next(0, 1);  // 随机决定哪位玩家先走。
        }

        // 告知用户游戏是否已准备好。
        return Task.FromResult(_gameState);
    }

    // 在游戏中落子。
    public async Task<GameState> MakeMove(GameMove move)
    {
        // 检查本次落子是否合法。
        if (_gameState is not GameState.InPlay) throw new ApplicationException("This game is not in play");

        if (_playerIds.IndexOf(move.PlayerId) < 0) throw new ArgumentException("No such playerid for this game", "move");
        if (move.PlayerId != _playerIds[_indexNextPlayerToMove]) throw new ArgumentException("The wrong player tried to make a move", "move");

        if (move.X < 0 || move.X > 2 || move.Y < 0 || move.Y > 2) throw new ArgumentException("Bad co-ordinates for a move", "move");
        if (_board[move.X, move.Y] != -1) throw new ArgumentException("That square is not empty", "move");

        // 记录落子。
        _moves.Add(move);
        _board[move.X, move.Y] = _indexNextPlayerToMove;

        // 检查本次落子是否获胜。
        var win = false;
        for (var i = 0; i < 3 && !win; i++)
        {
            win = IsWinningLine(_board[i, 0], _board[i, 1], _board[i, 2]);
        }

        if (!win)
        {
            for (var i = 0; i < 3 && !win; i++)
            {
                win = IsWinningLine(_board[0, i], _board[1, i], _board[2, i]);
            }
        }

        if (!win)
        {
            win = IsWinningLine(_board[0, 0], _board[1, 1], _board[2, 2]);
        }

        if (!win)
        {
            win = IsWinningLine(_board[0, 2], _board[1, 1], _board[2, 0]);
        }

        // 检查是否平局。
        var draw = false;
        if (_moves.Count is 9)
        {
            draw = true;  // 如果需要，也可以更早尝试检测无法获胜的僵局。
        }

        // 处理游戏结束。
        if (win || draw)
        {
            // 游戏结束。
            _gameState = GameState.Finished;
            if (win)
            {
                _winnerId = _playerIds[_indexNextPlayerToMove];
                _loserId = _playerIds[(_indexNextPlayerToMove + 1) % 2];
            }

            // 收集任务，以便同时等待两个通知操作。
            var promises = new List<Task>();
            // 告知此玩家游戏结果。
            var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(_playerIds[_indexNextPlayerToMove]);
            promises.Add(playerGrain.LeaveGame(this.GetPrimaryKey(), win ? GameOutcome.Win : GameOutcome.Draw));

            // 告知另一玩家游戏结果。
            playerGrain = GrainFactory.GetGrain<IPlayerGrain>(_playerIds[(_indexNextPlayerToMove + 1) % 2]);
            promises.Add(playerGrain.LeaveGame(this.GetPrimaryKey(), win ? GameOutcome.Lose : GameOutcome.Draw));
            await Task.WhenAll(promises);
            return _gameState;
        }

        // 如果游戏尚未结束，则准备让下一位玩家落子。
        _indexNextPlayerToMove = (_indexNextPlayerToMove + 1) % 2;
        return _gameState;
    }

    private static bool IsWinningLine(int i, int j, int k) => (i, j, k) switch
    {
        (0, 0, 0) => true,
        (1, 1, 1) => true,
        _ => false
    };


    public Task<GameState> GetState() => Task.FromResult(_gameState);

    public Task<List<GameMove>> GetMoves() => Task.FromResult(_moves);

    public async Task<GameSummary> GetSummary(Guid player)
    {
        var promises = new List<Task<string>>();
        foreach (var p in _playerIds.Where(p => p != player))
        {
            promises.Add(GrainFactory.GetGrain<IPlayerGrain>(p).GetUsername());
        }

        await Task.WhenAll(promises);

        return new GameSummary
        {
            NumMoves = _moves.Count,
            State = _gameState,
            YourMove = _gameState is GameState.InPlay && player == _playerIds[_indexNextPlayerToMove],
            NumPlayers = _playerIds.Count,
            GameId = this.GetPrimaryKey(),
            Usernames = promises.Select(x => x.Result).ToArray(),
            Name = _name,
            GameStarter = _playerIds.FirstOrDefault() == player
        };
    }

    public Task SetName(string name)
    {
        _name = name;
        return Task.CompletedTask;
    }
}
