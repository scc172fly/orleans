using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Durable.Messaging.Tests.Support;

public sealed class HandlerProbe
{
    private readonly ConcurrentDictionary<(GrainId GrainId, string Route), Barrier> _barriers = new();

    public Barrier Arm(GrainId grainId, string route, bool observeCancellation = true)
    {
        var barrier = new Barrier(this, (grainId, route), observeCancellation);
        if (!_barriers.TryAdd((grainId, route), barrier))
        {
            throw new InvalidOperationException($"A handler barrier is already armed for '{grainId}' and route '{route}'.");
        }

        return barrier;
    }

    public bool TryGet(
        GrainId grainId,
        string route,
        [NotNullWhen(true)] out Barrier? barrier) =>
        _barriers.TryGetValue((grainId, route), out barrier);

    public sealed class Barrier : IDisposable
    {
        private readonly HandlerProbe _owner;
        private readonly (GrainId GrainId, string Route) _key;
        private readonly bool _observeCancellation;

        internal Barrier(HandlerProbe owner, (GrainId GrainId, string Route) key, bool observeCancellation)
        {
            _owner = owner;
            _key = key;
            _observeCancellation = observeCancellation;
        }

        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilEnteredAsync() => Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        public void Release() => Continue.TrySetResult();

        // 一个忽略取消的屏障将处理程序阻塞至调用 Release 为止，即使关闭之后也是如此。
        internal Task WaitAsync(CancellationToken cancellationToken) =>
            _observeCancellation ? Continue.Task.WaitAsync(cancellationToken) : Continue.Task;

        public void Dispose()
        {
            Release();
            _owner._barriers.TryRemove(new KeyValuePair<(GrainId GrainId, string Route), Barrier>(_key, this));
        }
    }
}
