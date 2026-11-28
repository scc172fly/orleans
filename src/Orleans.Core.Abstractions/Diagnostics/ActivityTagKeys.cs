namespace Orleans.Diagnostics;

/// <summary>
/// 包含 Orleans 中使用的 Activity 标签键常量。
/// </summary>
internal static class ActivityTagKeys
{
    /// <summary>
    /// 异步枚举操作的请求 ID。
    /// </summary>
    public const string AsyncEnumerableRequestId = "orleans.async_enumerable.request_id";

    /// <summary>
    /// 激活实例 ID 标签键。
    /// </summary>
    public const string ActivationId = "orleans.activation.id";

    /// <summary>
    /// 激活原因标签键（例如“new”或“rehydrate”）。
    /// </summary>
    public const string ActivationCause = "orleans.activation.cause";

    /// <summary>
    /// 停用原因标签键。
    /// </summary>
    public const string DeactivationReason = "orleans.deactivation.reason";

    /// <summary>
    /// Grain ID 标签键。
    /// </summary>
    public const string GrainId = "orleans.grain.id";

    /// <summary>
    /// Grain 类型标签键。
    /// </summary>
    public const string GrainType = "orleans.grain.type";

    /// <summary>
    /// Grain 类型标签键。
    /// </summary>
    public const string GrainState = "orleans.grain.state";

    /// <summary>
    /// Silo ID 标签键。
    /// </summary>
    public const string SiloId = "orleans.silo.id";

    /// <summary>
    /// 目录中是否存在先前注册记录的标签键。
    /// </summary>
    public const string DirectoryPreviousRegistrationPresent = "orleans.directory.previousRegistration.present";

    /// <summary>
    /// 目录中已注册地址的标签键。
    /// </summary>
    public const string DirectoryRegisteredAddress = "orleans.directory.registered.address";

    /// <summary>
    /// 目录中转发地址的标签键。
    /// </summary>
    public const string DirectoryForwardingAddress = "orleans.directory.forwarding.address";

    /// <summary>
    /// 异常类型标签键。
    /// </summary>
    public const string ExceptionType = "exception.type";

    /// <summary>
    /// 异常消息标签键。
    /// </summary>
    public const string ExceptionMessage = "exception.message";

    /// <summary>
    /// 放置筛选器类型标签键。
    /// </summary>
    public const string PlacementFilterType = "orleans.placement.filter.type";

    /// <summary>
    /// 存储提供程序标签键。
    /// </summary>
    public const string StorageProvider = "orleans.storage.provider";

    /// <summary>
    /// 存储状态名称标签键。
    /// </summary>
    public const string StorageStateName = "orleans.storage.state.name";

    /// <summary>
    /// 存储状态类型标签键。
    /// </summary>
    public const string StorageStateType = "orleans.storage.state.type";

    /// <summary>
    /// RPC 系统名称标签键。
    /// </summary>
    public const string RpcSystem = "rpc.system.name";

    /// <summary>
    /// Orleans RPC 服务标签键。
    /// </summary>
    public const string RpcService = "orleans.rpc.service";

    /// <summary>
    /// RPC 方法标签键。
    /// </summary>
    public const string RpcMethod = "rpc.method";

    /// <summary>
    /// Orleans RPC 目标 ID 标签键。
    /// </summary>
    public const string RpcOrleansTargetId = "orleans.rpc.target_id";

    /// <summary>
    /// Orleans RPC 源 ID 标签键。
    /// </summary>
    public const string RpcOrleansSourceId = "orleans.rpc.source_id";

    /// <summary>
    /// 异常堆栈跟踪标签键。
    /// </summary>
    public const string ExceptionStacktrace = "exception.stacktrace";

    /// <summary>
    /// 异常是否传播出当前跟踪跨度的标签键。
    /// </summary>
    public const string ExceptionEscaped = "exception.escaped";

    /// <summary>
    /// 指示是否忽略了一次状态恢复尝试。
    /// </summary>
    public const string RehydrateIgnored = "orleans.rehydrate.ignored";

    /// <summary>
    /// 忽略状态恢复尝试的原因。
    /// </summary>
    public const string RehydrateIgnoredReason = "orleans.rehydrate.ignored.reason";

    /// <summary>
    /// 状态恢复期间的先前注册地址。
    /// </summary>
    public const string RehydratePreviousRegistration = "orleans.rehydrate.previousRegistration";

    /// <summary>
    /// 迁移的目标 Silo 地址。
    /// </summary>
    public const string MigrationTargetSilo = "orleans.migration.target.silo";

    /// <summary>
    /// 持久化作业 ID 标签键。
    /// </summary>
    public const string DurableJobId = "orleans.durable_job.id";

    /// <summary>
    /// 持久化作业名称标签键。
    /// </summary>
    public const string DurableJobName = "orleans.durable_job.name";

    /// <summary>
    /// 持久化作业分片 ID 标签键。
    /// </summary>
    public const string DurableJobShardId = "orleans.durable_job.shard_id";

    /// <summary>
    /// 持久化作业目标 Grain ID 标签键。
    /// </summary>
    public const string DurableJobTargetGrainId = "orleans.durable_job.target_grain_id";

    /// <summary>
    /// 持久化作业出队次数标签键。
    /// </summary>
    public const string DurableJobDequeueCount = "orleans.durable_job.dequeue_count";

    /// <summary>
    /// 持久化作业运行 ID 标签键。
    /// </summary>
    public const string DurableJobRunId = "orleans.durable_job.run_id";

    /// <summary>
    /// 持久化作业到期时间标签键。
    /// </summary>
    public const string DurableJobDueTime = "orleans.durable_job.due_time";

    /// <summary>
    /// 持久化作业状态标签键。
    /// </summary>
    public const string DurableJobStatus = "orleans.durable_job.status";

    /// <summary>
    /// 日志 ID 标签键。
    /// </summary>
    public const string JournalId = "orleans.journal.id";

    /// <summary>
    /// 日志存储操作标签键（append、snapshot、replace、delete、read）。
    /// </summary>
    public const string JournalStorageOperation = "orleans.journal.operation";

    /// <summary>
    /// 日志存储操作涉及的字节数。
    /// </summary>
    public const string JournalStorageBytes = "orleans.journal.bytes";

    /// <summary>
    /// 合并到一次日志存储写入中的待处理操作数。
    /// </summary>
    public const string JournalStorageBatchSize = "orleans.journal.batch_size";
}
