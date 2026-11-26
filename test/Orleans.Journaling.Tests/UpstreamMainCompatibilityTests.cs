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
/// 跨转复原测试:输入从 <c>upstream/main</c>
/// (通过PR的作者使用所留 <c>StateMachineManager</c> +内嵌的 <c>Durable*</c> 作家)
/// <see cref="JournaledStateManager"/> 与 OrleansBinary 并声称回收的物
/// 模拟状态符合主写内容 。
/// </summary>
/// <remarks>
/// <c>fixtures</c> 下的二进制固定质是由
/// <c>CaptureCrossVersionRecoveryFixture.EmitMainJournalHex</c> 测试正在对
/// 上/上层(<c>5989958561</c>。捕获练习的每一 <c>Durable*</c> 类型,并按
/// 流序登记令,因此日记流号(8.14)与以下的登记一致。
/// 故意将电线格式改变为 OrleansBinary cocs, 重运行
/// 固定在固定在上游/主要地带的工作树上,替换固定字节并接受更新的
/// 以 <c>snapshots</c>下进行核实。
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
        // 登记令登记令必须与捕获固定线匹配,以便日记流识别号(8.14)
        // 与上游/主要内容一致。
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
