using Orleans.Providers;
using Orleans.Serialization;
using UnitTests.GrainInterfaces;

namespace TestGrains
{
    // 一致日志的变量 grain 用于测试各种供应商和配置

    // 使用“正流存储”和明确配置的自定义软件
    [StorageProvider(ProviderName = "AzureStore")]
    [LogConsistencyProvider(ProviderName = "StateStorage")]
    public class LogTestGrainSharedStateStorage : LogTestGrain
    {
    }

    // 使用“正流存储”和明确配置的自定义软件
    [StorageProvider(ProviderName = "AzureStore")]
    [LogConsistencyProvider(ProviderName = "LogStorage")]
    public class LogTestGrainSharedLogStorage : LogTestGrain
    {
    }

    // 使用默认存储提供方作为共享存储
    public class LogTestGrainDefaultStorage : LogTestGrain
    {
    }

    // 使用 MemoryStore (使用 GSI grain)
    [StorageProvider(ProviderName = "MemoryStore")]
    public class LogTestGrainMemoryStorage : LogTestGrain
    {
    }

    // 使用所有组群均具有对称访问权限的、直白指定的 "CustomStorage" 日志一致性提供方
    [LogConsistencyProvider(ProviderName = "CustomStorage")]
    public class LogTestGrainCustomStorage : LogTestGrain,
        Orleans.EventSourcing.CustomStorage.ICustomStorageInterface<MyGrainState, object>
    {

        // 我们用这个 grain 的另外一根断线作为主电路。
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

    // 使用只从主集中访问的、 Exlicit 指定 "CustomStorage" 逻辑一致性的用户名
    [LogConsistencyProvider(ProviderName = "CustomStoragePrimaryCluster")]
    public class LogTestGrainCustomStoragePrimaryCluster : LogTestGrain,
        Orleans.EventSourcing.CustomStorage.ICustomStorageInterface<MyGrainState, object>
    {
        private readonly DeepCopier<MyGrainState> copier;

        // 我们用假的存储状态
        private MyGrainState? state;
        private int version;

        public LogTestGrainCustomStoragePrimaryCluster(DeepCopier<MyGrainState> copier)
        {
            this.copier = copier;
        }

        // 激活时模拟一个自动同步呼叫。 这在先前版本中造成僵局,
        // 所以我在这里加上它 来赶上倒退
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
            return Task.FromResult(new KeyValuePair<int, MyGrainState>(version, this.copier.Copy(state!)!)); // 国家在上述分支中初始化。
        }

        public Task ClearStoredState()
        {
            state = null;
            version = 0;
            return Task.CompletedTask;
        }
    }


}
