using Orleans.Concurrency;
using Orleans.Providers;

namespace TestGrains
{
    // 定义 CountersGrain 的四种变体，各自采用不同的配置。
    //
    // 所有变体都使用 SlowMemoryStore 存储提供程序，它将所有存储访问延迟 10 ms，
    // 以模拟云存储的延迟
    //
    // 其他配置涵盖以下选项的全部四种组合：
    // 状态存储与日志存储
    // 可重入与不可重入

    /// 使用 StateStorage 一致性提供程序时，仅持久化最新状态……我们并非
    /// 真正进行“事件溯源”，因为不打算持久化
    /// 事件，而只持久化状态快照。
    /// 
    /// 使用 LogStorage 一致性提供程序时，持久化日志，
    /// 即所有事件的完整序列

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
