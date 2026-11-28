using Newtonsoft.Json;

#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.Cosmos;
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.Cosmos;
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.Cosmos;
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.Cosmos;
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.Cosmos;
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif

internal abstract class BaseEntity
{
    internal const string ID_FIELD = "id";
    internal const string ETAG_FIELD = "_etag";

    [JsonProperty(ID_FIELD)]
    [JsonPropertyName(ID_FIELD)]
    public string Id { get; set; } = default!;

    [JsonProperty(ETAG_FIELD)]
    [JsonPropertyName(ETAG_FIELD)]
    public string? ETag { get; set; }
}
