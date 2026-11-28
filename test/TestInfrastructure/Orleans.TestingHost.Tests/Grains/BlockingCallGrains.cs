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
    /// 普通 Grain，在测试释放它之前一直阻塞，使调用的响应保持未完成状态，
    /// 从而让调用方的调用保持进行中。状态以 Grain 的主键为键，因此各测试与其他测试使用的
    /// 进程级静态状态相互隔离。
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
    /// 无状态工作者（放置在调用方本地），它发出出站调用并等待
    /// 响应。若在此调用进行中终止其 Silo，Grain 上下文的释放过程会
    /// 等待此工作者停用。若首次调用在关闭期间失败，工作者会重试，
    /// 以验证关闭时清理回调之后不能再注册新回调。
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
    /// 用于触发无状态工作者调用的普通 Grain（按标识放置），使工作者
    /// 与此 Grain 位于同一 Silo，从而为测试提供一个确定的待终止 Silo。
    /// </summary>
    public class LauncherGrain : Grain, ILauncherGrain
    {
        public Task<string> GetSiloIdentity() =>
            Task.FromResult(this.ServiceProvider.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString());

        public Task StartBlockingCall(ILocalWorkerGrain worker, IRemoteBlockerGrain remote)
        {
            // 发起调用后不等待结果，因此此 Grain 本身不会阻塞。无状态工作者放置在
            // 此 Silo 上，并将阻塞等待远程响应。
            _ = worker.CallRemote(remote);
            return Task.CompletedTask;
        }
    }
}
