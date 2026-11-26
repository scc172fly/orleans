using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Internal;

namespace Orleans.Runtime
{
    internal partial class AsyncTimer : IAsyncTimer
    {
        /// <summary>
        /// 计时器可在发出警告之前提前3秒钟点火,而且情况被认为不健康。
        /// </summary>
        private static readonly TimeSpan TimerDelaySlack = TimeSpan.FromSeconds(3);

        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
            Justification = "Dispose atomically takes the source with Interlocked.Exchange, then disposes the returned source in finally.")]
        private CancellationTokenSource? cancellation = new CancellationTokenSource();
        private readonly TimeSpan period;
        private readonly string name;
        private readonly ILogger log;
        private readonly TimeProvider _timeProvider;
        private DateTime lastFired = DateTime.MinValue;
        private DateTime expected;

        public AsyncTimer(TimeSpan period, string name, ILogger log, TimeProvider timeProvider)
        {
            this.log = log;
            this.period = period;
            this.name = name;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// 返回在规定延迟后完成的任务。
        /// </summary>
        /// <param name="overrideDelay">用于此定时器配置的期间的可选覆盖。</param>
        /// <returns><see langword="true"/> 如果计时器完成或 <see langword="false"/> 如果取消计时器</returns>
        public async Task<bool> NextTick(TimeSpan? overrideDelay = default)
        {
            var source = Volatile.Read(ref cancellation);
            if (source is null) return false;

            CancellationToken cancellationToken;
            lock (source)
            {
                // 同步获取待发物品并进行处置;此待发物品在事后仍可用。
                if (Volatile.Read(ref cancellation) is null) return false;
                cancellationToken = source.Token;
            }

            var start = _timeProvider.GetUtcNow().UtcDateTime;
            var delay = overrideDelay switch
            {
                { } value => value,
                _ when lastFired == DateTime.MinValue => period,
                _ when period == Timeout.InfiniteTimeSpan => period,
                _ when start - lastFired < period => period - (start - lastFired),
                _ => TimeSpan.Zero,
            };

            var isInfinite = delay == Timeout.InfiniteTimeSpan;
            if (delay < TimeSpan.Zero && !isInfinite) delay = TimeSpan.Zero;

            var hasAbsoluteDueTime = !isInfinite && delay <= DateTime.MaxValue - start;
            var dueTime = hasAbsoluteDueTime ? start.Add(delay) : DateTime.MaxValue;
            this.expected = dueTime;
            if (delay > TimeSpan.Zero || isInfinite)
            {
                try
                {
                    if (hasAbsoluteDueTime)
                    {
                        await _timeProvider.DelayUntilAsync(new DateTimeOffset(dueTime), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _timeProvider.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await Task.Yield();
                    expected = default;
                    return false;
                }
            }

            var now = this.lastFired = _timeProvider.GetUtcNow().UtcDateTime;
            var overshoot = GetOvershootDelay(now, dueTime);
            if (overshoot > TimeSpan.Zero)
            {
                if (!Debugger.IsAttached)
                {
                    LogTimerOvershoot(this.log, dueTime, now, overshoot);
                }
            }

            expected = default;
            return true;
        }

        private static TimeSpan GetOvershootDelay(DateTime now, DateTime dueTime)
        {
            if (dueTime == default) return TimeSpan.Zero;
            if (dueTime > now) return TimeSpan.Zero;

            var overshoot = now.Subtract(dueTime);
            if (overshoot > TimerDelaySlack) return overshoot;

            return TimeSpan.Zero;
        }

        public bool CheckHealth(DateTime lastCheckTime, [MaybeNullWhen(true)] out string reason)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var due = this.expected;
            var overshoot = GetOvershootDelay(now, due);
            if (overshoot > TimeSpan.Zero && !Debugger.IsAttached)
            {
                reason = $"{this.name} timer should have fired at {due}, which is {overshoot} ago";
                return false;
            }

            reason = default;
            return true;
        }

        public void Dispose()
        {
            var source = Interlocked.Exchange(ref cancellation, null);
            if (source is null) return;

            this.expected = default;
            try
            {
                source.Cancel();
            }
            finally
            {
                lock (source)
                {
                    source.Dispose();
                }
            }
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Timer should have fired at {DueTime} but fired at {CurrentTime}, which is {Overshoot} longer than expected"
        )]
        private static partial void LogTimerOvershoot(ILogger logger, DateTime dueTime, DateTime currentTime, TimeSpan overshoot);
    }
}
