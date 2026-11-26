namespace Orleans.Runtime;

/// <summary>
/// 定义 Orleans 运行时所排放活动的名称。
/// </summary>
public static class ActivityNames
{
    /// <summary>
    /// 选择 a 时所排放的活动的名称 silo a 的a grain.
    /// </summary>
    public const string PlaceGrain = "place grain";

    /// <summary>
    /// 过滤职位安置候选人时所排放活动的名称。
    /// </summary>
    public const string FilterPlacementCandidates = "filter placement candidates";

    /// <summary>
    /// 激活 grain时所排放的活动的名称。
    /// </summary>
    public const string ActivateGrain = "activate grain";

    /// <summary>
    /// 注销 grain时所排放活动的名称。
    /// </summary>
    public const string DeactivateGrain = "deactivate grain";

    /// <summary>
    /// 援引 grain激活回调时所释放的活动的名称。
    /// </summary>
    public const string OnActivate = "execute OnActivateAsync";

    /// <summary>
    /// 援引 grain的取消回调时所释放的活动的名称。
    /// </summary>
    public const string OnDeactivate = "execute OnDeactivateAsync";

    /// <summary>
    /// 登记时所排放的活动的名称 grain 激活 grain 目录。
    /// </summary>
    public const string RegisterDirectoryEntry = "register directory entry";

    /// <summary>
    /// 读取存储状态 grain 时所排放的活动的名称。
    /// </summary>
    public const string StorageRead = "read storage";

    /// <summary>
    /// 写入 grain 存储状态时所排放的活动的名称。
    /// </summary>
    public const string StorageWrite = "write storage";

    /// <summary>
    /// 储存时清除 grain 状态时所排放的活动的名称。
    /// </summary>
    public const string StorageClear = "clear storage";

    /// <summary>
    /// 在为移民而启动 grain 活化时脱水时所排放活动的名称。
    /// </summary>
    public const string ActivationDehydrate = "dehydrate activation";

    /// <summary>
    /// 在移动 grain 激活时补水时所释放的活动的名称。
    /// </summary>
    public const string ActivationRehydrate = "rehydrate activation";

    /// <summary>
    /// 等待 grain 迁移完成时所排放的活动的名称。
    /// </summary>
    public const string WaitMigration = "wait migration";
}
