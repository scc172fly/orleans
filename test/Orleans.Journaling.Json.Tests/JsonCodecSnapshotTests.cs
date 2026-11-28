using System.Buffers;
using System.Text;
using System.Text.Json;
using Orleans.Durable.Journaling.Tests;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Buffers.Adaptors;
using Xunit;
using static VerifyXunit.Verifier;

namespace Orleans.Durable.Journaling.Json.Tests;

/// <summary>
/// 快照测试，用于固定新的 <c>Json*CommandCodec</c>
/// 实现（位于 <see cref="Orleans.Durable.Journaling.Json"/> 下）生成的 JSONL 磁盘存储格式。
/// </summary>
/// <remarks>
/// JSONL 是新的默认日志格式（此分支更改了注册）。每项测试：
/// 1. 通过真实的编解码器将单个操作写入 <see cref="JsonLinesJournalFormat"/> 批次，
/// 2. 使用格式读取器读回生成的字节，并将其应用到会记录操作的状态对象，以证明
/// 这些字节仍可以解析并应用，并且
/// 3. 通过 Verify.Xunit 为生成的 UTF-8 文本创建快照
/// （文件 <c>snapshots/&lt;TestName&gt;.verified.jsonl</c>）。此快照是规范的
/// 人类可读视图：任何格式变化都将以 JSON 行文本差异的形式使测试失败。
/// </remarks>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public sealed class JsonCodecSnapshotTests
{
    private const uint SnapshotStreamId = 8;

    private static readonly JsonSerializerOptions Options = new() { TypeInfoResolver = JsonCodecTestJsonContext.Default };

    private static readonly JournalingSnapshotRecord SampleRecord = new(
        "snapshot-record",
        7,
        new JournalingSnapshotRecordTag("tag", 99));

    private static readonly JournalingSnapshotRecord SampleRecordAlt = new(
        "alt-record",
        13,
        new JournalingSnapshotRecordTag("alt-tag", 17));

    // --- 字典 ----------------------------------------------------------------------------

    [Fact]
    public Task Dictionary_Set_Primitives() =>
        SnapshotDictionaryOperation<string, int>(
            (codec, writer) => codec.WriteSet("alpha", 42, writer),
            ["set:alpha:42"]);

    [Fact]
    public Task Dictionary_Set_Record() =>
        SnapshotDictionaryOperation<string, JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSet("alpha", SampleRecord, writer),
            [$"set:alpha:{SampleRecord}"]);

    [Fact]
    public Task Dictionary_Remove_Primitives() =>
        SnapshotDictionaryOperation<string, int>(
            (codec, writer) => codec.WriteRemove("alpha", writer),
            ["remove:alpha"]);

    [Fact]
    public Task Dictionary_Remove_Record() =>
        SnapshotDictionaryOperation<string, JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteRemove("alpha", writer),
            ["remove:alpha"]);

    [Fact]
    public Task Dictionary_Clear_Primitives() =>
        SnapshotDictionaryOperation<string, int>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Dictionary_Clear_Record() =>
        SnapshotDictionaryOperation<string, JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Dictionary_Snapshot_Primitives() =>
        SnapshotDictionaryOperation<string, int>(
            (codec, writer) => codec.WriteSnapshot([new("alpha", 1), new("beta", 2)], writer),
            ["reset:2", "set:alpha:1", "set:beta:2"]);

    [Fact]
    public Task Dictionary_Snapshot_Record() =>
        SnapshotDictionaryOperation<string, JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([new("alpha", SampleRecord), new("beta", SampleRecordAlt)], writer),
            ["reset:2", $"set:alpha:{SampleRecord}", $"set:beta:{SampleRecordAlt}"]);

    [Fact]
    public Task Dictionary_SnapshotEmpty_Primitives() =>
        SnapshotDictionaryOperation<string, int>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task Dictionary_SnapshotEmpty_Record() =>
        SnapshotDictionaryOperation<string, JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task Dictionary_MultiOperation_Sequence()
    {
        var codec = new JsonDurableDictionaryCommandCodec<string, int>(Options);
        var state = new RecordingDictionaryState<string, int>(codec);
        return SnapshotMultiOp(
            codec,
            state,
            writer =>
            {
                codec.WriteSet("alpha", 1, writer);
                codec.WriteSet("beta", 2, writer);
                codec.WriteRemove("alpha", writer);
                codec.WriteSnapshot([new("beta", 2), new("gamma", 3)], writer);
            },
            ["set:alpha:1", "set:beta:2", "remove:alpha", "reset:2", "set:beta:2", "set:gamma:3"],
            () => state.Commands);
    }

    // --- 列表 ----------------------------------------------------------------------------------

    [Fact]
    public Task List_Add_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteAdd("one", writer),
            ["add:one"]);

    [Fact]
    public Task List_Add_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteAdd(SampleRecord, writer),
            [$"add:{SampleRecord}"]);

    [Fact]
    public Task List_Set_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteSet(0, "updated", writer),
            ["set:0:updated"]);

    [Fact]
    public Task List_Set_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSet(0, SampleRecord, writer),
            [$"set:0:{SampleRecord}"]);

    [Fact]
    public Task List_Insert_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteInsert(1, "two", writer),
            ["insert:1:two"]);

    [Fact]
    public Task List_Insert_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteInsert(1, SampleRecord, writer),
            [$"insert:1:{SampleRecord}"]);

    [Fact]
    public Task List_RemoveAt_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteRemoveAt(0, writer),
            ["remove:0"]);

    [Fact]
    public Task List_RemoveAt_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteRemoveAt(0, writer),
            ["remove:0"]);

    [Fact]
    public Task List_Clear_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task List_Clear_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task List_Snapshot_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteSnapshot(["three", "four"], writer),
            ["reset:2", "add:three", "add:four"]);

    [Fact]
    public Task List_Snapshot_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([SampleRecord, SampleRecordAlt], writer),
            ["reset:2", $"add:{SampleRecord}", $"add:{SampleRecordAlt}"]);

    [Fact]
    public Task List_SnapshotEmpty_Primitives() =>
        SnapshotListOperation<string>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task List_SnapshotEmpty_Record() =>
        SnapshotListOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task List_MultiOperation_Sequence()
    {
        var codec = new JsonDurableListCommandCodec<string>(Options);
        var state = new RecordingListState<string>(codec);
        return SnapshotMultiOp(
            codec,
            state,
            writer =>
            {
                codec.WriteAdd("one", writer);
                codec.WriteAdd("two", writer);
                codec.WriteSet(1, "updated", writer);
                codec.WriteInsert(0, "head", writer);
                codec.WriteRemoveAt(2, writer);
                codec.WriteSnapshot(["alpha", "beta"], writer);
            },
            ["add:one", "add:two", "set:1:updated", "insert:0:head", "remove:2", "reset:2", "add:alpha", "add:beta"],
            () => state.Commands);
    }

    // --- 队列 ---------------------------------------------------------------------------------

    [Fact]
    public Task Queue_Enqueue_Primitives() =>
        SnapshotQueueOperation<int>(
            (codec, writer) => codec.WriteEnqueue(10, writer),
            ["enqueue:10"]);

    [Fact]
    public Task Queue_Enqueue_Record() =>
        SnapshotQueueOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteEnqueue(SampleRecord, writer),
            [$"enqueue:{SampleRecord}"]);

    [Fact]
    public Task Queue_Dequeue_Primitives() =>
        SnapshotQueueOperation<int>(
            (codec, writer) => codec.WriteDequeue(writer),
            ["dequeue"]);

    [Fact]
    public Task Queue_Dequeue_Record() =>
        SnapshotQueueOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteDequeue(writer),
            ["dequeue"]);

    [Fact]
    public Task Queue_Clear_Primitives() =>
        SnapshotQueueOperation<int>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Queue_Clear_Record() =>
        SnapshotQueueOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Queue_Snapshot_Primitives() =>
        SnapshotQueueOperation<int>(
            (codec, writer) => codec.WriteSnapshot([20, 30], writer),
            ["reset:2", "enqueue:20", "enqueue:30"]);

    [Fact]
    public Task Queue_Snapshot_Record() =>
        SnapshotQueueOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([SampleRecord, SampleRecordAlt], writer),
            ["reset:2", $"enqueue:{SampleRecord}", $"enqueue:{SampleRecordAlt}"]);

    [Fact]
    public Task Queue_SnapshotEmpty_Primitives() =>
        SnapshotQueueOperation<int>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task Queue_SnapshotEmpty_Record() =>
        SnapshotQueueOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task Queue_MultiOperation_Sequence()
    {
        var codec = new JsonDurableQueueCommandCodec<int>(Options);
        var state = new RecordingQueueState<int>(codec);
        return SnapshotMultiOp(
            codec,
            state,
            writer =>
            {
                codec.WriteEnqueue(10, writer);
                codec.WriteEnqueue(20, writer);
                codec.WriteDequeue(writer);
                codec.WriteSnapshot([30, 40], writer);
            },
            ["enqueue:10", "enqueue:20", "dequeue", "reset:2", "enqueue:30", "enqueue:40"],
            () => state.Commands);
    }

    // --- 集合 -----------------------------------------------------------------------------------

    [Fact]
    public Task Set_Add_Primitives() =>
        SnapshotSetOperation<string>(
            (codec, writer) => codec.WriteAdd("alpha", writer),
            ["add:alpha"]);

    [Fact]
    public Task Set_Add_Record() =>
        SnapshotSetOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteAdd(SampleRecord, writer),
            [$"add:{SampleRecord}"]);

    [Fact]
    public Task Set_Remove_Primitives() =>
        SnapshotSetOperation<string>(
            (codec, writer) => codec.WriteRemove("alpha", writer),
            ["remove:alpha"]);

    [Fact]
    public Task Set_Remove_Record() =>
        SnapshotSetOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteRemove(SampleRecord, writer),
            [$"remove:{SampleRecord}"]);

    [Fact]
    public Task Set_Clear_Primitives() =>
        SnapshotSetOperation<string>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Set_Clear_Record() =>
        SnapshotSetOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Set_Snapshot_Primitives() =>
        SnapshotSetOperation<string>(
            (codec, writer) => codec.WriteSnapshot(["alpha", "beta"], writer),
            ["reset:2", "add:alpha", "add:beta"]);

    [Fact]
    public Task Set_Snapshot_Record() =>
        SnapshotSetOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([SampleRecord, SampleRecordAlt], writer),
            ["reset:2", $"add:{SampleRecord}", $"add:{SampleRecordAlt}"]);

    [Fact]
    public Task Set_SnapshotEmpty_Primitives() =>
        SnapshotSetOperation<string>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task Set_SnapshotEmpty_Record() =>
        SnapshotSetOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSnapshot([], writer),
            ["reset:0"]);

    [Fact]
    public Task Set_MultiOperation_Sequence()
    {
        var codec = new JsonDurableSetCommandCodec<string>(Options);
        var state = new RecordingSetState<string>(codec);
        return SnapshotMultiOp(
            codec,
            state,
            writer =>
            {
                codec.WriteAdd("alpha", writer);
                codec.WriteAdd("beta", writer);
                codec.WriteRemove("alpha", writer);
                codec.WriteSnapshot(["beta", "gamma"], writer);
            },
            ["add:alpha", "add:beta", "remove:alpha", "reset:2", "add:beta", "add:gamma"],
            () => state.Commands);
    }

    // --- 值 ---------------------------------------------------------------------------------

    [Fact]
    public Task Value_Set_Primitives()
    {
        var codec = new JsonDurableValueCommandCodec<int>(Options);
        var state = new RecordingValueState<int>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => codec.WriteSet(42, writer),
            () => Assert.Equal(42, state.Value));
    }

    [Fact]
    public Task Value_Set_Record()
    {
        var codec = new JsonDurableValueCommandCodec<JournalingSnapshotRecord>(Options);
        var state = new RecordingValueState<JournalingSnapshotRecord>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => codec.WriteSet(SampleRecord, writer),
            () => Assert.Equal(SampleRecord, state.Value));
    }

    // --- 状态 ---------------------------------------------------------------------------------

    [Fact]
    public Task State_Set_Primitives() =>
        SnapshotStateOperation<string>(
            (codec, writer) => codec.WriteSet("active", 7, writer),
            ["set:active:7"]);

    [Fact]
    public Task State_Set_Record() =>
        SnapshotStateOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteSet(SampleRecord, 7, writer),
            [$"set:{SampleRecord}:7"]);

    [Fact]
    public Task State_Clear_Primitives() =>
        SnapshotStateOperation<string>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task State_Clear_Record() =>
        SnapshotStateOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    // --- TaskCompletionSource ------------------------------------------------------------------

    [Fact]
    public Task Tcs_Pending_Primitives() =>
        SnapshotTcsOperation<int>(
            (codec, writer) => codec.WritePending(writer),
            ["pending"]);

    [Fact]
    public Task Tcs_Pending_Record() =>
        SnapshotTcsOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WritePending(writer),
            ["pending"]);

    [Fact]
    public Task Tcs_Completed_Primitives() =>
        SnapshotTcsOperation<int>(
            (codec, writer) => codec.WriteCompleted(99, writer),
            ["completed:99"]);

    [Fact]
    public Task Tcs_Completed_Record() =>
        SnapshotTcsOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteCompleted(SampleRecord, writer),
            [$"completed:{SampleRecord}"]);

    [Fact]
    public Task Tcs_Faulted_Primitives() =>
        SnapshotTcsOperation<int>(
            (codec, writer) => codec.WriteFaulted(MakeStableFault(), writer),
            ["faulted:snapshot-faulted-message"]);

    [Fact]
    public Task Tcs_Faulted_Record() =>
        SnapshotTcsOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteFaulted(MakeStableFault(), writer),
            ["faulted:snapshot-faulted-message"]);

    [Fact]
    public Task Tcs_Canceled_Primitives() =>
        SnapshotTcsOperation<int>(
            (codec, writer) => codec.WriteCanceled(writer),
            ["canceled"]);

    [Fact]
    public Task Tcs_Canceled_Record() =>
        SnapshotTcsOperation<JournalingSnapshotRecord>(
            (codec, writer) => codec.WriteCanceled(writer),
            ["canceled"]);

    // --- 日志 -----------------------------------------------------------------------------------

    private static readonly LogDraftId SnapshotDraftId = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), 3);

    [Fact]
    public Task Log_Append_Primitives() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteAppend(["alpha", "beta"], writer),
            ["append:alpha", "append:beta"]);

    [Fact]
    public Task Log_DraftCommit_Primitives() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteDraftCommit(SnapshotDraftId, 5, ["alpha"], writer),
            ["append:alpha", $"commitDraft:{SnapshotDraftId}:5:1"]);

    [Fact]
    public Task Log_DraftCommitEmpty_Primitives() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteDraftCommit(SnapshotDraftId, 5, [], writer),
            [$"commitDraft:{SnapshotDraftId}:5:0"]);

    [Fact]
    public Task Log_TrimBefore() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteTrimBefore(300, writer),
            ["trimBefore:300"]);

    [Fact]
    public Task Log_Clear() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteClear(writer),
            ["clear"]);

    [Fact]
    public Task Log_Seal() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteSeal(writer),
            ["seal"]);

    [Fact]
    public Task Log_Snapshot_Primitives() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteSnapshot(9, 4, 2, isSealed: true, ["alpha", "beta"], [new(SnapshotDraftId, 5, 1)], writer),
            ["snapshot:9:4:2:2", "append:alpha", "append:beta", $"commitDraft:{SnapshotDraftId}:5:1", "seal"]);

    [Fact]
    public Task Log_SnapshotEmpty_Primitives() =>
        SnapshotLogOperation<string>(
            (codec, writer) => codec.WriteSnapshot(0, 0, 0, isSealed: false, [], [], writer),
            ["snapshot:0:0:0:0"]);

    // --- 跨组件通用事项 -------------------------------------------------------------------------

    [Fact]
    public Task EmptyBatch()
    {
        // 未写入任何操作 → 已提交缓冲区为空 UTF-8 字符串。JSONL 没有每批次的
        // 头部，因此此处应不包含任何字节；明确固定此行为可以捕获意外的
        // “总是输出 BOM 或头部”行为在 JsonLinesJournalBufferWriter 中引入的回归问题。
        var format = new JsonLinesJournalFormat();
        using var writer = format.CreateWriter();
        var bytes = SnapshotBytes(writer);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(string.Empty, text);
        return VerifyJsonSnapshot(text);
    }

    [Fact]
    public void Determinism_RepeatedWritesProduceIdenticalText()
    {
        // Utf8JsonWriter 具有确定性；此冒烟测试用于防止未来基础设施
        // 变更（例如时间戳、ID 驻留）向 JSONL 输出引入非确定性。
        var first = WriteOnce();
        var second = WriteOnce();
        Assert.Equal(first, second);

        string WriteOnce()
        {
            var codec = new JsonDurableDictionaryCommandCodec<string, JournalingSnapshotRecord>(Options);
            var format = new JsonLinesJournalFormat();
            using var writer = format.CreateWriter();
            codec.WriteSet("alpha", SampleRecord, writer.CreateJournalStreamWriter(new JournalStreamId(SnapshotStreamId)));
            return Encoding.UTF8.GetString(SnapshotBytes(writer));
        }
    }

    // --- 内部机制 -----------------------------------------------------------------------------

    /// <summary>
    /// 使用一个新建且从未抛出的异常，以使快照不依赖实际堆栈跟踪或
    /// 捕获的目标方法；这些内容随运行时、构建配置和源码行号而变化。
    /// </summary>
    private static InvalidOperationException MakeStableFault() => new("snapshot-faulted-message");

    private Task SnapshotDictionaryOperation<TKey, TValue>(
        Action<JsonDurableDictionaryCommandCodec<TKey, TValue>, JournalStreamWriter> write,
        string[] expectedCommands)
        where TKey : notnull
    {
        var codec = new JsonDurableDictionaryCommandCodec<TKey, TValue>(Options);
        var state = new RecordingDictionaryState<TKey, TValue>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotListOperation<T>(
        Action<JsonDurableListCommandCodec<T>, JournalStreamWriter> write,
        string[] expectedCommands)
    {
        var codec = new JsonDurableListCommandCodec<T>(Options);
        var state = new RecordingListState<T>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotQueueOperation<T>(
        Action<JsonDurableQueueCommandCodec<T>, JournalStreamWriter> write,
        string[] expectedCommands)
    {
        var codec = new JsonDurableQueueCommandCodec<T>(Options);
        var state = new RecordingQueueState<T>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotSetOperation<T>(
        Action<JsonDurableSetCommandCodec<T>, JournalStreamWriter> write,
        string[] expectedCommands)
    {
        var codec = new JsonDurableSetCommandCodec<T>(Options);
        var state = new RecordingSetState<T>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotStateOperation<T>(
        Action<JsonPersistentStateCommandCodec<T>, JournalStreamWriter> write,
        string[] expectedCommands)
    {
        var codec = new JsonPersistentStateCommandCodec<T>(Options);
        var state = new RecordingStateState<T>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotTcsOperation<T>(
        Action<JsonDurableTaskCompletionSourceCommandCodec<T>, JournalStreamWriter> write,
        string[] expectedCommands)
    {
        var codec = new JsonDurableTaskCompletionSourceCommandCodec<T>(Options);
        var state = new RecordingTcsState<T>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotLogOperation<T>(
        Action<JsonDurableLogCommandCodec<T>, JournalStreamWriter> write,
        string[] expectedCommands)
    {
        var codec = new JsonDurableLogCommandCodec<T>(Options);
        var state = new RecordingLogState<T>(codec);
        return SnapshotSingleOp(
            codec,
            state,
            writer => write(codec, writer),
            () => Assert.Equal(expectedCommands, state.Commands));
    }

    private Task SnapshotSingleOp(
        object codec,
        IStateMachine state,
        Action<JournalStreamWriter> write,
        Action assertCommands)
    {
        var format = new JsonLinesJournalFormat();
        using var writer = format.CreateWriter();
        write(writer.CreateJournalStreamWriter(new JournalStreamId(SnapshotStreamId)));
        var bytes = SnapshotBytes(writer);
        var text = Encoding.UTF8.GetString(bytes);
        AssertNoCarriageReturns(text);
        ReadAndAssert(format, bytes, state, assertCommands);
        return VerifyJsonSnapshot(text);
    }

    private Task SnapshotMultiOp(
        object codec,
        IStateMachine state,
        Action<JournalStreamWriter> writeSequence,
        string[] expectedCommands,
        Func<IReadOnlyList<string>> getActualCommands)
    {
        var format = new JsonLinesJournalFormat();
        using var writer = format.CreateWriter();
        writeSequence(writer.CreateJournalStreamWriter(new JournalStreamId(SnapshotStreamId)));
        var bytes = SnapshotBytes(writer);
        var text = Encoding.UTF8.GetString(bytes);
        AssertNoCarriageReturns(text);
        ReadAndAssert(format, bytes, state, () => Assert.Equal(expectedCommands, getActualCommands()));
        return VerifyJsonSnapshot(text);
    }

    private static byte[] SnapshotBytes(JournalBufferWriter writer)
    {
        using var slice = writer.GetBuffer();
        return slice.ToArray();
    }

    /// <summary>
    /// 快照以 <c>eol=lf</c> 提交到 Git（参见 <c>.gitattributes</c>）；防止未来
    /// 变更在写入器输出中引入 <c>\r\n</c>，导致快照
    /// 依赖平台。
    /// </summary>
    private static void AssertNoCarriageReturns(string text) => Assert.DoesNotContain('\r', text);

    private static void ReadAndAssert(JsonLinesJournalFormat format, byte[] bytes, IStateMachine state, Action assertCommands)
    {
        if (bytes.Length == 0)
        {
            assertCommands();
            return;
        }

        using var writer = new ArcBufferWriter();
        writer.Write(bytes);
        var buffer = new JournalBufferReader(writer.Reader, isCompleted: true);
        var context = JournalTestReplayContext.Create(JsonLinesJournalFormat.JournalFormatKey, (new JournalStreamId(SnapshotStreamId), state));
        ((IJournalFormat)format).Replay(buffer, context);
        Assert.Equal(0, buffer.Length);
        assertCommands();
    }

    private static Task VerifyJsonSnapshot(string text) =>
        Verify(text, extension: "jsonl").UseDirectory("snapshots");
}
