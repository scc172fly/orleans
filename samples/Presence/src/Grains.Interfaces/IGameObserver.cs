namespace Presence.Grains;

/// <summary>
/// 观察者接口，供控制台应用或 Web 前端等外部客户端接收特定游戏的分数更新。
/// </summary>
public interface IGameObserver : IGrainObserver
{
    void UpdateGameScore(string score);
}
