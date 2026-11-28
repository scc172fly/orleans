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
                // 客户端断开连接后，.NET 垃圾回收器可以回收观察者对象。
                // 发生这种情况时，将停止观察。
                // 在此之前，定期向投票 Grain 发送心跳，以告知它我们仍在观察。
                if (_watcher.IsAlive)
                {
                    try
                    {
                        await _pollGrain.StartWatching(_watcherReference);
                    }
                    catch
                    {
                        // 忽略异常。应当记录此异常的日志。
                    }
                }
                else
                {
                    // 投票观察者对象已被回收，因此停止刷新其订阅。
                    break;
                }
            }

            // 通知投票 Grain，我们不再关注其更新。
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
                // TODO：记录日志。
            }

            _cancellation.Dispose();
        }
    }
}
