namespace Presence.Grains;

/// <summary>
/// 外部客户端(如控制台应用程序或网络前端)的观察界面,以接收特定游戏分数的最新信息。
/// </summary>
public interface IGameObserver : IGrainObserver
{
    void UpdateGameScore(string score);
}
