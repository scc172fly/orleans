using Orleans.Runtime;
using Orleans.Storage;


namespace Orleans.Persistence.Firestore;

/// <summary>
/// 配置 Google Cloud Firestore Grain 存储提供程序。
/// </summary>
public class FirestoreStateStorageOptions : FirestoreOptions, IStorageProviderSerializerOptions
{
    /// <summary>
    /// 指示 Grain 清除状态时应删除其数据还是将其重置为默认值。
    /// </summary>
    public bool DeleteStateOnClear { get; set; }

    /// <inheritdoc/>
    public IGrainStorageSerializer GrainStorageSerializer { get; set; } = default!;
}
