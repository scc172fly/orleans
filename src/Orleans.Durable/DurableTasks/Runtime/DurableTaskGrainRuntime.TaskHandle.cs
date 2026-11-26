using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Runtime;

internal sealed partial class DurableTaskGrainRuntime
{
    private class TaskHandle(
        TaskId taskId,
        DurableTaskGrainRuntime runtime,
        GrainId remoteTarget = default) : IScheduledTaskHandle
    {
        private readonly TaskCompletionSource<DurableTaskResponse> _responseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GrainId _remoteTarget = remoteTarget;

        public Task<DurableTaskResponse> ResponseTask => _responseTcs.Task;

        public TaskId TaskId { get; } = taskId;
        public GrainId RemoteTarget => _remoteTarget;

        // 如果这是假的, 任务已经从存储中补水, 但还没有开始运行 。
        // 如果任务无法连载, 就会发生这种情况, 就像当地的方法引用。
        public bool IsRunning { get; set; }

        public async ValueTask CancelAsync(string? reason, CancellationToken cancellationToken)
        {
            ValueTask cancellation;
            if (RemoteTarget.IsDefault)
            {
                cancellation = runtime.CancelScheduledTaskAsync(TaskId, reason, CancellationToken.None);
            }
            else
            {
                cancellation = runtime.CancelRemoteAsync(TaskId, RemoteTarget, reason, CancellationToken.None);
            }

            await cancellation.AsTask().WaitAsync(cancellationToken);
        }

        public async ValueTask AbortAsync(string reason, CancellationToken cancellationToken) =>
            await runtime.AbortTaskAsync(TaskId, reason, CancellationToken.None).AsTask().WaitAsync(cancellationToken);

        public async ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.PollTimeout > TimeSpan.Zero)
            {
                try
                {
                    await ((Task)ResponseTask)
                        .WaitAsync(options.PollTimeout, runtime._shared.TimeProvider, cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (TimeoutException)
                {
                    return DurableTaskResponse.Pending;
                }
            }

            if (ResponseTask.IsCompleted)
            {
                return await ResponseTask;
            }

            return DurableTaskResponse.Pending;
        }

        public async ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ResponseTask.WaitAsync(cancellationToken);
        }

        public bool TrySetResponse(DurableTaskResponse response) => _responseTcs.TrySetResult(response);

        /// <summary>使接柄服务员无法工作,因为激活停止,而任务在下一次激活时继续。</summary>
        /// <remarks>处理器可能没有服务员,所以可以观察到故障。</remarks>
        public void SetStopping(DurableTaskAttemptCanceledException exception)
        {
            if (_responseTcs.TrySetException(exception))
            {
                _ = _responseTcs.Task.Exception;
            }
        }
    }
}
