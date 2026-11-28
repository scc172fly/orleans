using System.Text;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using Orleans.TestingHost;

namespace Documentation.Grains.Journaling.Observation;

public interface IResponseGenerator
{
    IAsyncEnumerable<string> GenerateAsync(string prompt, CancellationToken cancellationToken);
}

// <conversation_grain_interface>
public interface IConversationGrain : IGrainWithStringKey
{
    ValueTask<long> Ask(string question, CancellationToken cancellationToken);

    // 结束对话。所有观察都会在最终条目之后结束。
    ValueTask End(CancellationToken cancellationToken);

    // 从指定位置读取已提交历史。世代编号为 0 时读取当前历史。
    IAsyncEnumerable<LogEvent<string>> WatchAsync(long epoch, long nextSequence, CancellationToken cancellationToken = default);

    // 已提交历史及流式草稿。
    IAsyncEnumerable<LogEvent<string>> WatchLiveAsync(
        long epoch, long nextSequence, LogDraftId? draftId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ValueChange<string>> WatchStatusAsync(CancellationToken cancellationToken = default);
}
// </conversation_grain_interface>

// <conversation_grain>
public sealed class ConversationGrain(
    IDurableStateManager stateManager,
    IResponseGenerator generator) : Grain, IConversationGrain
{
    private readonly IDurableLog<string> _messages = stateManager.GetOrAddLog<string>("messages");
    private readonly IDurableValue<string> _status = stateManager.GetOrAddValue<string>("status");

    public async ValueTask<long> Ask(string question, CancellationToken cancellationToken)
    {
        _messages.Append(question);
        _status.Value = "responding";
        await stateManager.WriteStateAsync(cancellationToken);

        // 包含草稿的读取方会在生成响应时接收每个词元。
        using var draft = _messages.BeginDraft();
        var response = new StringBuilder();
        try
        {
            await foreach (var token in generator.GenerateAsync(question, cancellationToken))
            {
                draft.Write(token);
                response.Append(token);
            }
        }
        catch (Exception exception)
        {
            draft.Abandon($"The response failed: {exception.Message}");
            _status.Value = "failed";
            await stateManager.WriteStateAsync(CancellationToken.None);
            throw;
        }

        // 已提交条目替换草稿的词元。
        var sequence = draft.Commit(response.ToString());
        _status.Value = "idle";
        await stateManager.WriteStateAsync(cancellationToken);
        return sequence;
    }

    public async ValueTask End(CancellationToken cancellationToken)
    {
        _messages.Seal();
        _status.Value = "ended";
        await stateManager.WriteStateAsync(cancellationToken);
    }

    public IAsyncEnumerable<LogEvent<string>> WatchAsync(long epoch, long nextSequence, CancellationToken cancellationToken = default) =>
        _messages.ReadAsync(nextSequence, epoch, cancellationToken: cancellationToken);

    public IAsyncEnumerable<LogEvent<string>> WatchLiveAsync(
        long epoch, long nextSequence, LogDraftId? draftId, CancellationToken cancellationToken = default) =>
        _messages.ReadAsync(nextSequence, epoch, includeDrafts: true, draftId, cancellationToken);

    public IAsyncEnumerable<ValueChange<string>> WatchStatusAsync(CancellationToken cancellationToken = default) =>
        _status.WatchAsync(cancellationToken: cancellationToken);
}
// </conversation_grain>

public static class ConversationClient
{
    // <conversation_client>
    public static async Task RenderAsync(IConversationGrain conversation, CancellationToken cancellationToken)
    {
        // 每个条目都已持久化，因此世代编号和最新条目之后的位置构成有效的恢复点。
        var epoch = 0L;
        var nextSequence = 0L;
        var history = new SortedList<long, string>();
        var drafts = new Dictionary<LogDraftId, StringBuilder>();
        await foreach (var logEvent in conversation.WatchLiveAsync(epoch, nextSequence, draftId: null, cancellationToken))
        {
            epoch = logEvent.Epoch;
            switch (logEvent.Kind)
            {
                case LogEventKind.Entry:
                    history[logEvent.Sequence] = logEvent.Item!;
                    nextSequence = logEvent.Sequence + 1;
                    Console.WriteLine($"[{logEvent.Sequence}] {logEvent.Item}");
                    break;
                case LogEventKind.CaughtUp:
                    Console.WriteLine($"Loaded {history.Count} messages.");
                    break;
                case LogEventKind.Trimmed:
                    // 较早的条目已被裁剪。较新的条目仍然有效。
                    while (history.Count > 0 && history.Keys[0] < logEvent.Sequence)
                    {
                        history.RemoveAt(0);
                    }

                    nextSequence = Math.Max(nextSequence, logEvent.Sequence);
                    break;
                case LogEventKind.Reset:
                    // 条目已被替换，例如由压实后的摘要替换，或因删除日志而消失。
                    history.Clear();
                    nextSequence = logEvent.Sequence;
                    break;
                case LogEventKind.DraftItem:
                    var draft = logEvent.Sequence == 0
                        ? drafts[logEvent.DraftId!.Value] = new StringBuilder()
                        : drafts[logEvent.DraftId!.Value];
                    draft.Append(logEvent.Item);
                    break;
                case LogEventKind.DraftCommitted:
                    // 草稿的词元由 [Sequence, Sequence + Count) 范围内的条目取代。
                    drafts.Remove(logEvent.DraftId!.Value);
                    break;
                case LogEventKind.DraftAbandoned:
                    drafts.Remove(logEvent.DraftId!.Value);
                    Console.WriteLine($"Response abandoned ({logEvent.AbandonReason}): {logEvent.Message}");
                    break;
                case LogEventKind.Sealed:
                    Console.WriteLine("The conversation ended.");
                    break;
            }
        }
    }
    // </conversation_client>
}

public sealed class FixedResponseGenerator(params string[] tokens) : IResponseGenerator
{
    public async IAsyncEnumerable<string> GenerateAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }
}

public static class ConversationGrainTests
{
    // <journaling_test_cluster>
    public static async Task AskCommitsTheResponseAndSurvivesReactivation()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((options, siloBuilder) =>
        {
            // 实际的日志状态管理器，日志保存在 Silo 内存中。基于反射的 JSON 元数据
            // 适用于测试；生产 Silo 应注册源生成的元数据。
            siloBuilder.AddJournaling()
                .UseJsonJournalFormat(new DefaultJsonTypeInfoResolver())
                .AddVolatileJournalStorage();
            siloBuilder.Services.AddSingleton<IResponseGenerator>(new FixedResponseGenerator("Hello", ", world"));
        });

        await using var cluster = builder.Build();
        await cluster.DeployAsync();
        var conversation = cluster.Client.GetGrain<IConversationGrain>("test");
        var sequence = await conversation.Ask("Hi", CancellationToken.None);

        // 新的激活实例从日志恢复对话。
        await cluster.DeactivateAsync(conversation);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var history = new List<string>();
        await foreach (var logEvent in conversation.WatchAsync(epoch: 0, nextSequence: 0, timeout.Token))
        {
            if (logEvent.Kind is LogEventKind.Entry)
            {
                history.Add(logEvent.Item!);
            }
            else if (logEvent.Kind is LogEventKind.CaughtUp)
            {
                break;
            }
        }

        if (sequence != 1 || !history.SequenceEqual(["Hi", "Hello, world"]))
        {
            throw new InvalidOperationException("The conversation did not record the committed response.");
        }
    }
    // </journaling_test_cluster>
}
