using Orleans.Providers;
using Orleans.Serialization;
using UnitTests.GrainInterfaces;

namespace TestGrains
{
    // 日志一致性 Grain 的各种变体用于测试不同的提供程序和配置

    // 使用 Azure 存储和显式配置的一致性提供程序
    [StorageProvider(ProviderName = "AzureStore")]
    [LogConsistencyProvider(ProviderName = "StateStorage")]
    public class LogTestGrainSharedStateStorage : LogTestGrain
    {
    }

    // 使用 Azure 存储和显式配置的一致性提供程序
    [StorageProvider(ProviderName = "AzureStore")]
    [LogConsistencyProvider(ProviderName = "LogStorage")]
    public class LogTestGrainSharedLogStorage : LogTestGrain
    {
    }

    // 使用默认存储提供程序作为共享存储
    public class LogTestGrainDefaultStorage : LogTestGrain
    {
    }

    // 使用 MemoryStore（它使用 GSI Grain）
    [StorageProvider(ProviderName = "MemoryStore")]
    public class LogTestGrainMemoryStorage : LogTestGrain
    {
    }

    // 使用显式指定的 "CustomStorage" 日志一致性提供程序，允许所有集群对称访问
    [LogConsistencyProvider(ProviderName = "CustomStorage")]
    public class LogTestGrainCustomStorage : LogTestGrain,
        Orleans.EventSourcing.CustomStorage.ICustomStorageInterface<MyGrainState, object>
    {

        // 使用此 Grain 的另一种实现作为主 Grain。
        private ILogTestGrain? storagegrain;

        private ILogTestGrain GetStorageGrain()
        {
            if (storagegrain == null)
            {
                storagegrain = GrainFactory.GetGrain<ILogTestGrain>(this.GetPrimaryKeyLong(), "TestGrains.LogTestGrainSharedStateStorage");
            }
            return storagegrain;
        }


        public Task<bool> ApplyUpdatesToStorage(IReadOnlyList<object> updates, int expectedversion)
        {
            return GetStorageGrain().Update(updates, expectedversion);
        }

        public async Task<KeyValuePair<int, MyGrainState>> ReadStateFromStorage()
        {
            var kvp = await GetStorageGrain().Read();
            return new KeyValuePair<int, MyGrainState>(kvp.Key, (MyGrainState)kvp.Value);
        }

        public Task ClearStoredState()
        {
            return GetStorageGrain().Clear();
        }
    }

    // 使用显式指定的 "CustomStorage" 日志一致性提供程序，仅允许主集群访问
    [LogConsistencyProvider(ProviderName = "CustomStoragePrimaryCluster")]
    public class LogTestGrainCustomStoragePrimaryCluster : LogTestGrain,
        Orleans.EventSourcing.CustomStorage.ICustomStorageInterface<MyGrainState, object>
    {
        private readonly DeepCopier<MyGrainState> copier;

        // 使用模拟的内存状态作为存储
        private MyGrainState? state;
        private int version;

        public LogTestGrainCustomStoragePrimaryCluster(DeepCopier<MyGrainState> copier)
        {
            this.copier = copier;
        }

        // 模拟激活期间的异步调用。此操作曾在早期版本导致死锁，
        // 所以在此加入测试以发现回归问题。
        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            await Task.Run(async () =>
            {
                await Task.Delay(10, cancellationToken);
            }, CancellationToken.None);
        }


        public Task<bool> ApplyUpdatesToStorage(IReadOnlyList<object> updates, int expectedversion)
        {
            if (state == null)
            {
                state = new MyGrainState();
                version = 0;
            }

            if (expectedversion != version)
                return Task.FromResult(false);

            foreach (var u in updates)
            {
                this.TransitionState(state, u);
                version++;
            }

            return Task.FromResult(true);
        }

        public Task<KeyValuePair<int, MyGrainState>> ReadStateFromStorage()
        {
            if (state == null)
            {
                state = new MyGrainState();
                version = 0;
            }
            return Task.FromResult(new KeyValuePair<int, MyGrainState>(version, this.copier.Copy(state!)!)); // 状态已在上面的分支中初始化。
        }

        public Task ClearStoredState()
        {
            state = null;
            version = 0;
            return Task.CompletedTask;
        }
    }


}
