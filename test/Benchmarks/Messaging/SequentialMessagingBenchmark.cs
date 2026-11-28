using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Durable.Messaging;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace Benchmarks.Messaging;

/// <summary>
/// 测量在一个小型 Grain 环中逐条传递并确认持久化消息的性能。
/// </summary>
[BenchmarkCategory("Messaging")]
public class SequentialMessagingBenchmark
{
    private const int MessagesPerInvocation = 1024;
    private InProcessTestCluster? _cluster;
    private CommittedHopProbe _probe = null!;
    private ISequentialMessagingGrain[] _grains = [];
    private int _completedRuns;

    /// <summary>获取或设置单个 Silo 上相互交互的 Grain 数量。</summary>
    [Params(2, 4, 8)]
    public int GrainCount { get; set; }

    /// <summary>在测量之外创建新的有界存储并对环进行预热。</summary>
    [IterationSetup]
    public void SetupIteration() => SetupIterationAsync().GetAwaiter().GetResult();

    private async Task SetupIterationAsync()
    {
        _probe = new CommittedHopProbe();
        _completedRuns = 0;
        var builder = new InProcessTestClusterBuilder(1);
        builder.Options.ConfigureFileLogging = false;
        builder.ConfigureHost(host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureSilo((_, silo) =>
        {
            silo.UseInMemoryDurableJobs();
            silo.AddVolatileJournalStorage();
            silo.AddMessaging();
            silo.Services.AddMessageType<SequentialMessage>(SequentialMessagingGrain.Subject);
            silo.Services.AddSingleton(_probe);
        });
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _grains = Enumerable.Range(0, GrainCount)
            .Select(index => _cluster.Client.GetGrain<ISequentialMessagingGrain>(index))
            .ToArray();
        for (var index = 0; index < _grains.Length; index++)
        {
            await _grains[index].ConfigureAsync(_grains[(index + 1) % _grains.Length].GetGrainId());
        }
        await RunChainAsync();
    }

    /// <summary>依次完成 1,024 次消息投递，并将测量结果归一化为一次已确认的消息投递。</summary>
    /// <returns>已提交的处理器执行步骤的确切数量。</returns>
    [Benchmark(OperationsPerInvoke = MessagesPerInvocation)]
    public Task<int> SequentialRing() => RunChainAsync();

    private async Task<int> RunChainAsync()
    {
        var runId = Guid.NewGuid();
        var completion = _probe.Begin(runId, MessagesPerInvocation);
        await _grains[0].StartAsync(runId, MessagesPerInvocation);
        int count;
        try
        {
            count = await completion.WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"Run {runId}, {GrainCount}-grain ring: awaiting journal acknowledgements, {_probe.AcknowledgedCount}/{MessagesPerInvocation} observed.",
                exception);
        }
        if (count != MessagesPerInvocation)
        {
            throw new InvalidOperationException($"Run {runId} committed {count} of {MessagesPerInvocation} messages.");
        }
        _completedRuns++;
        return count;
    }

    /// <summary>在测量之外检查业务副作用计数，排空并释放集群。</summary>
    [IterationCleanup]
    public void CleanupIteration() => CleanupIterationAsync().GetAwaiter().GetResult();

    private async Task CleanupIterationAsync()
    {
        if (_cluster is not { } cluster)
        {
            return;
        }

        try
        {
            var counts = await Task.WhenAll(_grains.Select(grain => grain.GetProcessedCountAsync()));
            var expected = (long)_completedRuns * MessagesPerInvocation;
            if (counts.Sum() != expected || counts.Any(count => count != expected / GrainCount))
            {
                throw new InvalidOperationException(
                    $"The {GrainCount}-grain ring applied [{string.Join(", ", counts)}]; expected {expected} total effects.");
            }
        }
        finally
        {
            await cluster.DisposeAsync();
            _cluster = null;
        }
    }

    /// <summary>释放因迭代初始化失败而遗留的所有者对象。</summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_cluster is { } cluster)
        {
            await cluster.DisposeAsync();
            _cluster = null;
        }
    }
}

/// <summary>配置持久化消息顺序传递环并初始化其数据。</summary>
public interface ISequentialMessagingGrain : IGrainWithIntegerKey
{
    /// <summary>在测量之前持久化下一个目标。</summary>
    Task ConfigureAsync(GrainId next);

    /// <summary>提交初始出站消息发送意图。</summary>
    Task StartAsync(Guid runId, int messageCount);

    /// <summary>返回累计已应用的处理器副作用数量。</summary>
    Task<long> GetProcessedCountAsync();
}

/// <summary>顺序传递链中的一条消息。</summary>
[GenerateSerializer]
public sealed record SequentialMessage(
    [property: Id(0)] Guid RunId,
    [property: Id(1)] int Hop,
    [property: Id(2)] int MessageCount);

/// <summary>使每一跳都经过生产环境所用的收件箱、发件箱、日志及持久化作业泵。</summary>
public sealed class SequentialMessagingGrain : Grain, ISequentialMessagingGrain
{
    /// <summary>顺序传递链中单次跳转所使用的确切协议主题。</summary>
    public const string Subject = "benchmarks.sequential-hop.v1";

    private readonly IOutbox _outbox;
    private readonly IDurableStateManager _state;
    private readonly IDurableValue<GrainId> _next;
    private readonly IDurableValue<long> _processed;
    private readonly IDurableValue<SequentialMessage> _progress;
    private readonly CommittedHopProbe _probe;
    private readonly MessageType<SequentialMessage> _type;

    /// <summary>构造环中的参与者及其确认观察者。</summary>
    public SequentialMessagingGrain(
        IInbox inbox,
        IOutbox outbox,
        IDurableStateManager state,
        IJournaledStateManager journal,
        [FromKeyedServices("ring-next")] IDurableValue<GrainId> next,
        [FromKeyedServices("ring-processed")] IDurableValue<long> processed,
        [FromKeyedServices("ring-progress")] IDurableValue<SequentialMessage> progress,
        CommittedHopProbe probe,
        [FromKeyedServices(Subject)] MessageType<SequentialMessage> type)
    {
        _outbox = outbox;
        _state = state;
        _next = next;
        _processed = processed;
        _progress = progress;
        _probe = probe;
        _type = type;
        inbox.Register(type, this,
            static (message, grain, context) => grain.HandleHop(message, context));
        journal.RegisterStateMachine("ring-acknowledgements", new HopObserver(this));
    }

    /// <inheritdoc/>
    public async Task ConfigureAsync(GrainId next)
    {
        _next.Value = next;
        await _state.WriteStateAsync();
    }

    /// <inheritdoc/>
    public async Task StartAsync(Guid runId, int messageCount)
    {
        SendHop(new SequentialMessage(runId, 0, messageCount));
        await _state.WriteStateAsync();
    }

    /// <inheritdoc/>
    public Task<long> GetProcessedCountAsync() => Task.FromResult(_processed.Value);

    private void HandleHop(SequentialMessage message, IInboxHandlerContext context)
    {
        if (message.RunId == Guid.Empty || message.Hop < 0 || message.Hop >= message.MessageCount)
        {
            throw new ArgumentException("The sequential message has an invalid run or hop.");
        }
        var nextCount = checked(_processed.Value + 1);
        if (message.Hop + 1 < message.MessageCount)
        {
            SendHop(message with { Hop = message.Hop + 1 });
        }
        _processed.Value = nextCount;
        _progress.Value = message;
        context.Complete();
    }

    private void SendHop(SequentialMessage message) =>
        _outbox.Send(_type, MessageId.Create(
            "runs", message.RunId.ToString("N"), "hops", message.Hop.ToString(CultureInfo.InvariantCulture)),
            _next.Value, message);

    // 每次写入日志时捕获进度，并在存储确认之后报告该进度。
    private sealed class HopObserver(SequentialMessagingGrain owner) : IStateMachine
    {
        private SequentialMessage? _captured;

        public void WritePendingEntries(JournalStreamWriter writer) => _captured = owner._progress.Value;
        public void WriteSnapshot(JournalStreamWriter writer) => _captured = owner._progress.Value;
        public void OnWriteCompleted()
        {
            if (_captured is { } message)
            {
                owner._probe.OnCommitted(message);
            }
            _captured = null;
        }
        public void ReplayEntry(JournalEntry entry, JournalReplayContext context) { }
        public void Reset(JournalStreamWriter writer) { }
    }
}

/// <summary>在实际收到日志写入确认后观察每一次跳转。</summary>
public sealed class CommittedHopProbe
{
    private readonly object _lock = new();
    private readonly HashSet<int> _acknowledged = [];
    private TaskCompletionSource<int> _completion = null!;
    private Guid _runId;
    private int _messageCount;

    /// <summary>获取当前传递链中不同日志写入确认的数量。</summary>
    public int AcknowledgedCount
    {
        get
        {
            lock (_lock)
            {
                return _acknowledged.Count;
            }
        }
    }

    /// <summary>在发送初始意图前准备好完成通知机制。</summary>
    public Task<int> Begin(Guid runId, int messageCount)
    {
        lock (_lock)
        {
            _runId = runId;
            _messageCount = messageCount;
            _acknowledged.Clear();
            _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _completion.Task;
        }
    }

    /// <summary>仅在每次不同的跳转均已确认后，才发出传递链完成的信号。</summary>
    public void OnCommitted(SequentialMessage message)
    {
        lock (_lock)
        {
            if (message.RunId == _runId && _acknowledged.Add(message.Hop)
                && _acknowledged.Count == _messageCount)
            {
                _completion.SetResult(_acknowledged.Count);
            }
        }
    }
}
