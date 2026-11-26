using Orleans.Runtime;
using Orleans.Storage;


namespace Orleans.Persistence.Firestore;

/// <summary>
/// 配置 Google Cloud Firshstore grain 存储供应商。
/// </summary>
public class FirestoreStateStorageOptions : FirestoreOptions, IStorageProviderSerializerOptions
{
    /// <summary>
    /// 表明 grain 数据应删除,或当 grain 澄清了它的状况。
    /// </summary>
    public bool DeleteStateOnClear { get; set; }

    /// <inheritdoc/>
    public IGrainStorageSerializer GrainStorageSerializer { get; set; } = default!;
}
