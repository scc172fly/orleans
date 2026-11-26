using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Orleans.TestingHost.Tests.Grains
{
    public interface IRemoteBlockerGrain : IGrainWithGuidKey
    {
        Task<string> GetSiloIdentity();
        Task BlockUntilReleased();
    }

    public interface ILocalWorkerGrain : IGrainWithGuidKey
    {
        Task CallRemote(IRemoteBlockerGrain remote);
    }

    public interface ILauncherGrain : IGrainWithGuidKey
    {
        Task<string> GetSiloIdentity();
        Task StartBlockingCall(ILocalWorkerGrain worker, IRemoteBlockerGrain remote);
    }

    /// <summary>
    /// A 常设经常定期 grain 将响应按住一个呼叫的响应, 以打开
    /// 调用者在飞行中停留。国家用 grain主密钥键系国家,因此每个测试都是孤立的
    /// 由其他方使用的全过程静态中产生的。
    /// </summary>
    public class RemoteBlockerGrain : Grain, IRemoteBlockerGrain
    {
        private sealed record BlockState(TaskCompletionSource Release, SemaphoreSlim Entered);

        private static readonly ConcurrentDictionary<Guid, BlockState> States = new();

        private static BlockState GetState(Guid key) =>
            States.GetOrAdd(key, _ => new BlockState(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new SemaphoreSlim(0)));

        public static void Release(Guid key) => GetState(key).Release.TrySetResult();

        public static Task<bool> WaitForEntered(Guid key, TimeSpan timeout)
            => WaitForEntered(key, timeout, CancellationToken.None);

        public static Task<bool> WaitForEntered(Guid key, TimeSpan timeout, CancellationToken cancellationToken)
            => GetState(key).Entered.WaitAsync(timeout, cancellationToken);

        public Task<string> GetSiloIdentity() =>
            Task.FromResult(this.ServiceProvider.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString());

        public Task BlockUntilReleased()
        {
            var state = GetState(this.GetPrimaryKey());
            state.Entered.Release();
            return state.Release.Task;
        }
    }

    /// <summary>
    /// 无国籍工人(当地人,由当地人打来),他发出外向的呼唤,等待着
    /// 。当其 silo 在呼声在飞行中时被杀死时, grain 上下文处置
    /// 如果在停工期间发生第一个呼叫故障,工人将重新计票。
    /// 以核实在关闭回击扫描后, 任何新的回调都无法登记 。
    /// </summary>
    [StatelessWorker(1)]
    public class LocalWorkerGrain : Grain, ILocalWorkerGrain
    {
        public async Task CallRemote(IRemoteBlockerGrain remote)
        {
            try
            {
                await remote.BlockUntilReleased();
            }
            catch (SiloUnavailableException)
            {
                await remote.BlockUntilReleased();
            }
        }
    }

    /// <summary>
    /// 通常使用一种(身份 -- -- 地点) grain ,触发无国籍工人的呼唤,因此该工人
    /// 将 grain silo放在此处,给测试一个确定性 silo 来杀死。
    /// </summary>
    public class LauncherGrain : Grain, ILauncherGrain
    {
        public Task<string> GetSiloIdentity() =>
            Task.FromResult(this.ServiceProvider.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString());

        public Task StartBlockingCall(ILocalWorkerGrain worker, IRemoteBlockerGrain remote)
        {
            // 火烧而忘记,所以这 grain 本身并不能阻挡。
            // 下个 silo 将阻挡远程响应
            _ = worker.CallRemote(remote);
            return Task.CompletedTask;
        }
    }
}
