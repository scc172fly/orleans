namespace Orleans.Runtime;

/// <summary>
/// 定义 Orleans 运行时发出的 Activity 的名称。
/// </summary>
public static class ActivityNames
{
    /// <summary>
    /// 为 Grain 选择 Silo 时发出的 Activity 名称。
    /// </summary>
    public const string PlaceGrain = "place grain";

    /// <summary>
    /// 筛选放置候选项时发出的 Activity 名称。
    /// </summary>
    public const string FilterPlacementCandidates = "filter placement candidates";

    /// <summary>
    /// 激活 Grain 时发出的 Activity 名称。
    /// </summary>
    public const string ActivateGrain = "activate grain";

    /// <summary>
    /// 停用 Grain 时发出的 Activity 名称。
    /// </summary>
    public const string DeactivateGrain = "deactivate grain";

    /// <summary>
    /// 调用 Grain 激活回调时发出的 Activity 名称。
    /// </summary>
    public const string OnActivate = "execute OnActivateAsync";

    /// <summary>
    /// 调用 Grain 停用回调时发出的 Activity 名称。
    /// </summary>
    public const string OnDeactivate = "execute OnDeactivateAsync";

    /// <summary>
    /// 将 Grain 激活实例注册到 Grain 目录时发出的 Activity 名称。
    /// </summary>
    public const string RegisterDirectoryEntry = "register directory entry";

    /// <summary>
    /// 从存储读取 Grain 状态时发出的 Activity 名称。
    /// </summary>
    public const string StorageRead = "read storage";

    /// <summary>
    /// 将 Grain 状态写入存储时发出的 Activity 名称。
    /// </summary>
    public const string StorageWrite = "write storage";

    /// <summary>
    /// 从存储清除 Grain 状态时发出的 Activity 名称。
    /// </summary>
    public const string StorageClear = "clear storage";

    /// <summary>
    /// 为迁移提取 Grain 激活实例状态时发出的 Activity 名称。
    /// </summary>
    public const string ActivationDehydrate = "dehydrate activation";

    /// <summary>
    /// 还原已迁移 Grain 激活实例状态时发出的 Activity 名称。
    /// </summary>
    public const string ActivationRehydrate = "rehydrate activation";

    /// <summary>
    /// 等待 Grain 迁移完成时发出的 Activity 名称。
    /// </summary>
    public const string WaitMigration = "wait migration";
}
