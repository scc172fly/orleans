using Orleans.Concurrency;

namespace TicTacToe.Grains;

/// <summary>
/// Orleans grain 执行类别 GameGrain
/// </summary>
[Reentrant]
public class GameGrain : Grain, IGameGrain
{
    // 当前游戏中玩家列表
    // 简单化,玩家 0 总是播放“ O” 和玩家 1 播放“ X”
    //  谁启动游戏, 游戏一旦启动即随机调用, 并且通过 indexNextPlayerToMove设定
    public List<Guid> _playerIds = new();
    private int _indexNextPlayerToMove;

    private GameState _gameState;
    private Guid _winnerId;
    private Guid _loserId;

    // 我们记录了每一动作的游戏, 这样我们就可以重建游戏序列
    // 在活动游戏中,我们使用 2D 数组代表棋盘,以形成
    //  更容易检查合法动作、 彩排等 。
    //  - 1 代表空方, 0 & 1 玩家索引
    private List<GameMove> _moves = new();
    private int[,] _board = null!;

    private string _name = null!;

    // 地址
    public override Task OnActivateAsync(CancellationToken token)
    {
        // 确定新建立的游戏状态正确
        _playerIds = new List<Guid>();
        _moves = new List<GameMove>();
        _indexNextPlayerToMove = -1;  // 安全安全默认 - 当游戏开始到 0或 1时设置安全缺省 -
        _board = new int[3, 3] { { -1, -1, -1 }, { -1, -1, -1 }, { -1, -1, -1 } };  // - 1 是空的

        _gameState = GameState.AwaitingPlayers;
        _winnerId = Guid.Empty;
        _loserId = Guid.Empty;

        return base.OnActivateAsync(token);
    }

    // 将玩家添加到游戏
    public Task<GameState> AddPlayerToGame(Guid player)
    {
        // 选中它是否可以加入此游戏
        if (_gameState is GameState.Finished) throw new ApplicationException("Can't join game once its over");
        if (_gameState is GameState.InPlay) throw new ApplicationException("Can't join game once its in play");

        // 添加播放器
        _playerIds.Add(player);

        // 选中此项游戏是否准备播放
        if (_gameState is GameState.AwaitingPlayers && _playerIds.Count is 2)
        {
            // 新游戏开始
            _gameState = GameState.InPlay;
            _indexNextPlayerToMove = Random.Shared.Next(0, 1);  // 随机任意排列到谁第一个移动
        }

        // 让用户知道游戏是否准备好
        return Task.FromResult(_gameState);
    }

    // 游戏中移动
    public async Task<GameState> MakeMove(GameMove move)
    {
        // 检查其要合法移动是否合法
        if (_gameState is not GameState.InPlay) throw new ApplicationException("This game is not in play");

        if (_playerIds.IndexOf(move.PlayerId) < 0) throw new ArgumentException("No such playerid for this game", "move");
        if (move.PlayerId != _playerIds[_indexNextPlayerToMove]) throw new ArgumentException("The wrong player tried to make a move", "move");

        if (move.X < 0 || move.X > 2 || move.Y < 0 || move.Y > 2) throw new ArgumentException("Bad co-ordinates for a move", "move");
        if (_board[move.X, move.Y] != -1) throw new ArgumentException("That square is not empty", "move");

        // 记录移动
        _moves.Add(move);
        _board[move.X, move.Y] = _indexNextPlayerToMove;

        // 中胜动作的检查
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

        // 抽图检查
        var draw = false;
        if (_moves.Count is 9)
        {
            draw = true;  // 如果我们愿意 我们可以尝试早点寻找僵局
        }

        // 游戏结束控点
        if (win || draw)
        {
            // 游戏结束
            _gameState = GameState.Finished;
            if (win)
            {
                _winnerId = _playerIds[_indexNextPlayerToMove];
                _loserId = _playerIds[(_indexNextPlayerToMove + 1) % 2];
            }

            // 集合任务, 所以我们同时等待两个通知
            var promises = new List<Task>();
            // 向此播放器告知结果
            var playerGrain = GrainFactory.GetGrain<IPlayerGrain>(_playerIds[_indexNextPlayerToMove]);
            promises.Add(playerGrain.LeaveGame(this.GetPrimaryKey(), win ? GameOutcome.Win : GameOutcome.Draw));

            // 向其他角色告知结果
            playerGrain = GrainFactory.GetGrain<IPlayerGrain>(_playerIds[(_indexNextPlayerToMove + 1) % 2]);
            promises.Add(playerGrain.LeaveGame(this.GetPrimaryKey(), win ? GameOutcome.Lose : GameOutcome.Draw));
            await Task.WhenAll(promises);
            return _gameState;
        }

        // 如果游戏还没有结束, 准备下一步玩家移动
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
