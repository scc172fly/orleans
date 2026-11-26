using System.Diagnostics.CodeAnalysis;

namespace Orleans.Streaming.AzureStorage.Providers.Streams.AzureQueue.Json;

/// <summary>
/// 数据适配器 Azure 队列 JSON 数据调配器的配置选项。
/// </summary>
[Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
public class AzureQueueJsonDataAdapterOptions
{
    /// <summary>
    /// 获取或设置值,以显示是否在 JSON 序列化失败时启用返回到二进制序列化。
    /// 默认为真 。
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// 获取或设定一个数值,表明是否偏向于先 JSON 序列化/脱序,然后返回二进制。
    /// 假时, 它将先尝试二进制序列/ 取消序列, 然后返回 JSON。
    /// </summary>
    public bool PreferJson { get; set; } = true;
}
