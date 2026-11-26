using Orleans.Concurrency;
using Orleans.Providers;

namespace TestGrains
{
    // 我们定义了 CountersGrain 中具有不同配置的四个变体。
    //
    // 所有变量都使用 SlowMemoryStore 存储提供者;它将所有存取延迟10米
    // 模拟云存储时的衬内
    //
    // 其他组合选择所有四种组合:
    // 状态与日志储存
    // 重新ENTRant 与非重新entrant 的对比

    /// 当使用 StateStorage 一致性提供商时,我们坚持仅最新状态.我们是 
    /// 而不是真正“活动来源”,因为我们不想坚持 
    /// 事件,但只有州级照片。
    /// 
    /// 当使用 LogStorage 一致性提供商时,我们坚持日志
    /// i.e. 所有事件的完整序列

    [LogConsistencyProvider(ProviderName = "StateStorage")]
    [StorageProvider(ProviderName = "SlowMemoryStore")]
    public class CountersGrain_StateStore_NonReentrant : CountersGrain
    {
    }

    [LogConsistencyProvider(ProviderName = "StateStorage")]
    [StorageProvider(ProviderName = "SlowMemoryStore")]
    [Reentrant]
    public class CountersGrain_StateStore_Reentrant : CountersGrain
    {
    }

    [LogConsistencyProvider(ProviderName = "LogStorage")]
    [StorageProvider(ProviderName = "SlowMemoryStore")]
    public class CountersGrain_LogStore_NonReentrant : CountersGrain
    {
    }

    [LogConsistencyProvider(ProviderName = "LogStorage")]
    [StorageProvider(ProviderName = "SlowMemoryStore")]
    [Reentrant]
    public class CountersGrain_LogStore_Reentrant : CountersGrain
    {
    }




}
