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

        // 如果此值为 false，则任务已从存储恢复，但尚未开始运行。
        // 如果任务不可序列化（例如本地方法调用），就会出现这种情况。
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

        /// <summary>因激活实例停止而使句柄的等待者失败，而任务在下一激活实例中继续。</summary>
        /// <remarks>句柄可能没有等待者，因此将该失败标记为已观察。</remarks>
        public void SetStopping(DurableTaskAttemptCanceledException exception)
        {
            if (_responseTcs.TrySetException(exception))
            {
                _ = _responseTcs.Task.Exception;
            }
        }
    }
}
