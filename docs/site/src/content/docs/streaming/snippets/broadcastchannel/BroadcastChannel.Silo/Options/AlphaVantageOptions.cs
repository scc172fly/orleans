namespace BroadcastChannel.Silo.Options;

public sealed class AlphaVantageOptions
{
    /// <summary>
    /// 在此获取免费 API 密钥：<a href="https://www.alphavantage.co/support/#api-key"></a>。
    /// 设置为名为 "AlphaVantageOptions__ApiKey" 的环境变量。
    /// </summary>
    public required string ApiKey { get; set; } = "demo";
}
