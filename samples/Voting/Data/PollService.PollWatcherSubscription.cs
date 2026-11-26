using VotingContract;

namespace Voting.Data;

public sealed partial class PollService
{
    private class PollWatcherSubscription : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly WeakReference _watcher;
        private readonly Task _watcherTask;
        private readonly IPollGrain _pollGrain;
        private readonly IPollWatcher _watcherReference;

        public PollWatcherSubscription(IPollWatcher watcher, IPollGrain pollGrain, IPollWatcher watcherReference)
        {
            _pollGrain = pollGrain;
            _watcher = new WeakReference(watcher);
            _watcherReference = watcherReference;
            _watcherTask = Task.Run(WatchPoll);
        }

        private async Task WatchPoll()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

            await _pollGrain.StartWatching(_watcherReference);
            while (await timer.WaitForNextTickAsync(_cancellation.Token))
            {
                // 当客户端断开时, .NET 垃圾收集器可以清理监视对象。
                // 当发生这种情况时,我们将停止观察。
                // 在此之前,定期进行投票 grain 让它知道我们仍在监视
                if (_watcher.IsAlive)
                {
                    try
                    {
                        await _pollGrain.StartWatching(_watcherReference);
                    }
                    catch
                    {
                        // 忽略例外,我们应该记录下来
                    }
                }
                else
                {
                    // 投票观察对象已被清理干净, 所以不要更新其订阅内容 。
                    break;
                }
            }

            // 通知民意调查 grain ,我们不再感兴趣
            _pollGrain.StopWatching(_watcherReference).Ignore();
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            try
            {
                await _watcherTask;
            }
            catch
            {
                // TOD: 日志
            }

            _cancellation.Dispose();
        }
    }
}
