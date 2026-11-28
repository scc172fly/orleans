using System.Diagnostics.CodeAnalysis;

namespace Orleans.Streaming.AzureStorage.Providers.Streams.AzureQueue.Json;

/// <summary>
/// Azure Queue JSON 数据适配器的配置选项。
/// </summary>
[Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
public class AzureQueueJsonDataAdapterOptions
{
    /// <summary>
    /// 获取或设置一个值，指示 JSON 序列化失败时是否启用回退到二进制序列化的机制。
    /// 默认为 true。
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// 获取或设置一个值，指示是否优先尝试 JSON 序列化或反序列化，再回退到二进制格式。
    /// 为 false 时，先尝试二进制序列化或反序列化，再回退到 JSON。
    /// </summary>
    public bool PreferJson { get; set; } = true;
}
