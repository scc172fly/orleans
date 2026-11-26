namespace Orleans.Diagnostics;

/// <summary>
/// 包含活动标签键的常数 Orleans.
/// </summary>
internal static class ActivityTagKeys
{
    /// <summary>
    /// 同步可计算操作的请求代号 。
    /// </summary>
    public const string AsyncEnumerableRequestId = "orleans.async_enumerable.request_id";

    /// <summary>
    /// 启动的ID标记密钥
    /// </summary>
    public const string ActivationId = "orleans.activation.id";

    /// <summary>
    /// 激活原因标记密钥 (e.g., “新”或“体液化剂”) 。
    /// </summary>
    public const string ActivationCause = "orleans.activation.cause";

    /// <summary>
    /// 禁用理由标记密钥 。
    /// </summary>
    public const string DeactivationReason = "orleans.deactivation.reason";

    /// <summary>
    /// grain ID标记密钥。
    /// </summary>
    public const string GrainId = "orleans.grain.id";

    /// <summary>
    /// grain 类型标记密钥。
    /// </summary>
    public const string GrainType = "orleans.grain.type";

    /// <summary>
    /// grain 类型标记密钥。
    /// </summary>
    public const string GrainState = "orleans.grain.state";

    /// <summary>
    /// silo ID标记密钥。
    /// </summary>
    public const string SiloId = "orleans.silo.id";

    /// <summary>
    /// 先前注册的目录显示标签密钥 。
    /// </summary>
    public const string DirectoryPreviousRegistrationPresent = "orleans.directory.previousRegistration.present";

    /// <summary>
    /// 目录注册的地址标记密钥 。
    /// </summary>
    public const string DirectoryRegisteredAddress = "orleans.directory.registered.address";

    /// <summary>
    /// 目录转发地址标记密钥 。
    /// </summary>
    public const string DirectoryForwardingAddress = "orleans.directory.forwarding.address";

    /// <summary>
    /// 例外类型标记密钥 。
    /// </summary>
    public const string ExceptionType = "exception.type";

    /// <summary>
    /// 例外信息标记密钥 。
    /// </summary>
    public const string ExceptionMessage = "exception.message";

    /// <summary>
    /// 放置过滤器类型标签密钥 。
    /// </summary>
    public const string PlacementFilterType = "orleans.placement.filter.type";

    /// <summary>
    /// 存储供应商标签密钥
    /// </summary>
    public const string StorageProvider = "orleans.storage.provider";

    /// <summary>
    /// 存储状态名标记密钥 。
    /// </summary>
    public const string StorageStateName = "orleans.storage.state.name";

    /// <summary>
    /// 存储状态标签密钥 。
    /// </summary>
    public const string StorageStateType = "orleans.storage.state.type";

    /// <summary>
    /// RPC 系统名称标记密钥。
    /// </summary>
    public const string RpcSystem = "rpc.system.name";

    /// <summary>
    /// Orleans RPC 服务标记密钥。
    /// </summary>
    public const string RpcService = "orleans.rpc.service";

    /// <summary>
    /// RPC 方法标记密钥 。
    /// </summary>
    public const string RpcMethod = "rpc.method";

    /// <summary>
    /// RPC Orleans 目标识别标记密钥 。
    /// </summary>
    public const string RpcOrleansTargetId = "orleans.rpc.target_id";

    /// <summary>
    /// RPC Orleans 源代码识别标记密钥。
    /// </summary>
    public const string RpcOrleansSourceId = "orleans.rpc.source_id";

    /// <summary>
    /// 例外的堆叠追踪标记密钥 。
    /// </summary>
    public const string ExceptionStacktrace = "exception.stacktrace";

    /// <summary>
    /// 例外的逃出标记密钥 。
    /// </summary>
    public const string ExceptionEscaped = "exception.escaped";

    /// <summary>
    /// 表明是否忽略了补液尝试。
    /// </summary>
    public const string RehydrateIgnored = "orleans.rehydrate.ignored";

    /// <summary>
    /// 体液补充企图被忽略的原因。
    /// </summary>
    public const string RehydrateIgnoredReason = "orleans.rehydrate.ignored.reason";

    /// <summary>
    /// 之前的体液补充期间登记地址。
    /// </summary>
    public const string RehydratePreviousRegistration = "orleans.rehydrate.previousRegistration";

    /// <summary>
    /// 目标 silo 针对移徙。
    /// </summary>
    public const string MigrationTargetSilo = "orleans.migration.target.silo";

    /// <summary>
    /// 持久工作标识标签密钥 。
    /// </summary>
    public const string DurableJobId = "orleans.durable_job.id";

    /// <summary>
    /// 耐久的职称标记密钥 。
    /// </summary>
    public const string DurableJobName = "orleans.durable_job.name";

    /// <summary>
    /// 持久工作舍弃标记键
    /// </summary>
    public const string DurableJobShardId = "orleans.durable_job.shard_id";

    /// <summary>
    /// 持久工作目标 grain ID标记键。
    /// </summary>
    public const string DurableJobTargetGrainId = "orleans.durable_job.target_grain_id";

    /// <summary>
    /// 持久工作取消计数标记键
    /// </summary>
    public const string DurableJobDequeueCount = "orleans.durable_job.dequeue_count";

    /// <summary>
    /// 持久工作运行的 ID 标签密钥 。
    /// </summary>
    public const string DurableJobRunId = "orleans.durable_job.run_id";

    /// <summary>
    /// 持久工作到期时间标记键 。
    /// </summary>
    public const string DurableJobDueTime = "orleans.durable_job.due_time";

    /// <summary>
    /// 持久工作身份标签键。
    /// </summary>
    public const string DurableJobStatus = "orleans.durable_job.status";

    /// <summary>
    /// 日记代号标签密钥 。
    /// </summary>
    public const string JournalId = "orleans.journal.id";

    /// <summary>
    /// 日记存储操作标签密钥(附录、快照、替换、删除、读)
    /// </summary>
    public const string JournalStorageOperation = "orleans.journal.operation";

    /// <summary>
    /// 日记存储操作所涉字节数 。
    /// </summary>
    public const string JournalStorageBytes = "orleans.journal.bytes";

    /// <summary>
    /// 有待完成的业务数量合并成一份日记存储书。
    /// </summary>
    public const string JournalStorageBatchSize = "orleans.journal.batch_size";
}
