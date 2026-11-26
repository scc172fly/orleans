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

    // 谈话结束,每只手表在最后进入后都结束了
    ValueTask End(CancellationToken cancellationToken);

    // 历史从一个位置上诞生,零时代是当前历史的代号。
    IAsyncEnumerable<LogEvent<string>> WatchAsync(long epoch, long nextSequence, CancellationToken cancellationToken = default);

    // 历史和流传的草案。
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

        // 包括草稿在内的读者在生成回复时收到每个象征性的信号。
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

        // 承诺条目替换草稿的标记 。
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
        // 每个条目都是耐久的,因此最近一个条目后的时代和位置是一个有效的恢复点。
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
                    // 旧条目被剪切。 后来的条目仍然有效 。
                    while (history.Count > 0 && history.Keys[0] < logEvent.Sequence)
                    {
                        history.RemoveAt(0);
                    }

                    nextSequence = Math.Max(nextSequence, logEvent.Sequence);
                    break;
                case LogEventKind.Reset:
                    // 条目被替换,例如用压缩摘要或删除日记。
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
                    // 草稿的标记被[序列、序列+计数]中的条目取代。
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
            // 实际出版的国家管理人员,期刊在 silo 反射 JSON 元元元元元数据
            // 套装测试;生产 silos 登记源生成元数据。
            siloBuilder.AddJournaling()
                .UseJsonJournalFormat(new DefaultJsonTypeInfoResolver())
                .AddVolatileJournalStorage();
            siloBuilder.Services.AddSingleton<IResponseGenerator>(new FixedResponseGenerator("Hello", ", world"));
        });

        await using var cluster = builder.Build();
        await cluster.DeployAsync();
        var conversation = cluster.Client.GetGrain<IConversationGrain>("test");
        var sequence = await conversation.Ask("Hi", CancellationToken.None);

        // 新的激活程序从日记上恢复了谈话。
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
