using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Core;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;
using Xunit;
using static VerifyXunit.Verifier;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 跨版本恢复测试：将从 <c>upstream/main</c> 捕获的预先准备好的日志二进制数据
/// （使用旧版 <c>StateMachineManager</c> 及内联的 <c>Durable*</c> 写入器）交给此 PR 的
/// 使用 OrleansBinary 编解码器的 <see cref="JournaledStateManager"/> 处理，并断言恢复后的
/// 内存状态与 main 写入的内容一致。
/// </summary>
/// <remarks>
/// <c>fixtures</c> 下的二进制测试数据由
/// 针对 upstream/main 运行的 <c>CaptureCrossVersionRecoveryFixture.EmitMainJournalHex</c> 测试生成
/// （提交 <c>5989958561</c>）。捕获过程覆盖每种 <c>Durable*</c> 类型，并固定
/// 流注册顺序，使日志流 ID（8..14）与下方的注册对应。
/// 若在有意修改 OrleansBinary 编解码器的传输格式后需要重新生成数据，请重新运行该
/// 测试夹具（在固定到 upstream/main 的工作树中），替换测试数据字节，并接受
/// <c>snapshots</c> 下更新后的 Verify 快照。
/// </remarks>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public sealed class UpstreamMainCompatibilityTests : JournalingTestBase
{
    [Fact]
    public async Task OrleansBinary_RecoversUpstreamMainWrittenJournal()
    {
        var journalBytes = LoadUpstreamMainJournal();
        var storage = await new VolatileJournalStorage().ReadForWriteAsync();
        await storage.AppendAsync(new ReadOnlySequence<byte>(journalBytes), TestContext.Current.CancellationToken);

        var states = CreateStates(storage);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await states.Manager.InitializeAsync(cts.Token);

        Assert.Equal(2, states.Dictionary.Count);
        Assert.Equal(1, states.Dictionary["alpha"]);
        Assert.Equal(2, states.Dictionary["beta"]);

        Assert.Equal(["first", "second", "third"], states.List);

        Assert.Equal(2, states.Queue.Count);
        Assert.Equal(10, states.Queue.Dequeue());
        Assert.Equal(20, states.Queue.Dequeue());

        Assert.True(states.Set.SetEquals(["foo", "bar"]));

        Assert.Equal(42, states.Value.Value);

        Assert.Equal("hello", ((IStorage<string>)states.State).State);

        Assert.Equal(DurableTaskCompletionSourceStatus.Completed, states.Tcs.State.Status);
        Assert.Equal(99, states.Tcs.State.Value);
        Assert.Equal(99, await states.Tcs.Task);

        await Verify(JournalSnapshotFormatting.FormatBinary(journalBytes), extension: "txt").UseDirectory("snapshots");
    }

    private static byte[] LoadUpstreamMainJournal([CallerFilePath] string sourceFile = "")
    {
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "fixtures",
            "UpstreamMainCompatibilityTests.upstream-main-journal.bin");

        return File.ReadAllBytes(fixturePath);
    }

    private DurableStates CreateStates(IJournalStorage storage)
    {
        var manager = CreateManager(storage);
        // 注册顺序必须与捕获测试数据时的顺序一致，以使日志流 ID（8..14）
        // 与 upstream/main 写入的内容对应。
        return new DurableStates(
            manager,
            new DurableDictionary<string, int>("dict", manager,
                new OrleansBinaryDurableDictionaryCommandCodec<string, int>(ValueCodec<string>(), ValueCodec<int>(), SessionPool)),
            new DurableList<string>("list", manager,
                new OrleansBinaryDurableListCommandCodec<string>(ValueCodec<string>(), SessionPool)),
            new DurableQueue<int>("queue", manager,
                new OrleansBinaryDurableQueueCommandCodec<int>(ValueCodec<int>(), SessionPool)),
            new DurableSet<string>("set", manager,
                new OrleansBinaryDurableSetCommandCodec<string>(ValueCodec<string>(), SessionPool)),
            new DurableValue<int>("value", manager,
                new OrleansBinaryDurableValueCommandCodec<int>(ValueCodec<int>(), SessionPool)),
            new JournaledPersistentState<string>("state", manager,
                new OrleansBinaryPersistentStateCommandCodec<string>(ValueCodec<string>(), SessionPool), CodecProvider.GetActivator<string>()),
            new DurableTaskCompletionSource<int>(
                "tcs",
                manager,
                new OrleansBinaryDurableTaskCompletionSourceCommandCodec<int>(ValueCodec<int>(), ValueCodec<Exception>(), SessionPool),
                Copier<int>(),
                Copier<Exception>()));
    }

    private JournaledStateManager CreateManager(IJournalStorage storage)
    {
        var shared = new JournaledStateManagerShared(
            LoggerFactory.CreateLogger<JournaledStateManager>(),
            Options.Create(ManagerOptions),
            TimeProvider.System,
            ServiceProvider);

        return new(shared, storage);
    }

    private IFieldCodec<T> ValueCodec<T>() => CodecProvider.GetCodec<T>();

    private DeepCopier<T> Copier<T>() => ServiceProvider.GetRequiredService<DeepCopier<T>>();

    private sealed record DurableStates(
        JournaledStateManager Manager,
        DurableDictionary<string, int> Dictionary,
        DurableList<string> List,
        DurableQueue<int> Queue,
        DurableSet<string> Set,
        DurableValue<int> Value,
        JournaledPersistentState<string> State,
        DurableTaskCompletionSource<int> Tcs);
}
