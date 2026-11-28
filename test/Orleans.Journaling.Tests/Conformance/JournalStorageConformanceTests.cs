using System.Buffers;
using System.Security.Cryptography;
using CsCheck;
using Orleans.Storage;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 验证 <see cref="IJournalStorage"/> 契约。本仓库中的每个日志存储
/// 提供程序都运行这套测试。
/// </summary>
/// <remarks>
/// 读取操作将日志内容作为一个字节序列返回，因此这些测试通过比较该序列与
/// 契约允许可见的完整段的拼接结果，来验证段的原子性及顺序。
/// </remarks>
public abstract class JournalStorageConformanceTests : IAsyncLifetime
{
    private IJournalStorageProvider? _provider;
    private IJournalStorageProvider? _faultingProvider;

    /// <summary>
    /// 获取注入由 <see cref="CreateFaultingStorage"/> 创建的存储实例中的故障。
    /// </summary>
    protected JournalStorageFaultInjector Faults { get; } = new();

    /// <summary>
    /// 获取单次 <see cref="IJournalStorage.AppendAsync"/> 接受的最大段大小；若为 <see langword="null"/>，则表示
    /// 提供程序没有自行施加单次追加限制。
    /// </summary>
    protected virtual long? MaxAppendBytes => null;

    /// <summary>
    /// 获取 <see cref="CreateProviderAsync"/> 在收到 <see cref="JournalStorageFaultInjector"/> 时注入的故障。
    /// </summary>
    protected virtual IReadOnlySet<JournalStorageFaultKind> SupportedFaults { get; } = new HashSet<JournalStorageFaultKind>();

    private bool SupportsFaultInjection => SupportedFaults.Count > 0;

    /// <summary>
    /// 获取基于模型的测试所运行的随机操作序列数量。
    /// </summary>
    protected virtual int PropertyIterations => 25;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// 创建提供程序。同一个测试类实例创建的提供程序共享同一底层存储。
    /// </summary>
    /// <param name="faults">要注入提供程序写入请求的故障；没有故障时为 <see langword="null"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected abstract ValueTask<IJournalStorageProvider> CreateProviderAsync(JournalStorageFaultInjector? faults, CancellationToken cancellationToken);

    public virtual async ValueTask InitializeAsync()
    {
        _provider = await CreateProviderAsync(faults: null, CancellationToken);
        if (SupportsFaultInjection)
        {
            _faultingProvider = await CreateProviderAsync(Faults, CancellationToken);
        }
    }

    public virtual ValueTask DisposeAsync() => default;

    [Fact]
    public async Task Append_ReadReturnsWholeSegmentsInWriteOrder()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        byte[][] segments = [Bytes(3, 1), Bytes(70_000, 2), Bytes(1, 3), Bytes(4_096, 4)];
        foreach (var segment in segments)
        {
            await writer.AppendAsync(new ReadOnlySequence<byte>(segment), CancellationToken);
        }

        Assert.Equal(Concat(segments), await ReadContentAsync(CreateStorage(journalId)));
        Assert.Equal(Concat(segments), await ReadContentAsync(writer));
    }

    [Fact]
    public async Task Append_MultiSegmentSequence_IsStoredAsOneSegment()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        var first = new SequenceSegment(Bytes(5, 1));
        var last = first.Append(Bytes(7, 2));

        await writer.AppendAsync(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length), CancellationToken);

        Assert.Equal(Concat([Bytes(5, 1), Bytes(7, 2)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Replace_FollowedByAppends_ReadReturnsReplacementThenAppends()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(10, 1), CancellationToken);
        await writer.AppendAsync(Sequence(20, 2), CancellationToken);

        await writer.ReplaceAsync(Sequence(100_000, 3), CancellationToken);
        await writer.AppendAsync(Sequence(30, 4), CancellationToken);
        await writer.AppendAsync(Sequence(40, 5), CancellationToken);

        var expected = Concat([Bytes(100_000, 3), Bytes(30, 4), Bytes(40, 5)]);
        Assert.Equal(expected, await ReadContentAsync(CreateStorage(journalId)));

        var reader = CreateStorage(journalId);
        Assert.Equal(expected, await ReadContentAsync(reader));
        await reader.ReplaceAsync(Sequence(1, 6), CancellationToken);
        await reader.AppendAsync(Sequence(2, 7), CancellationToken);
        Assert.Equal(Concat([Bytes(1, 6), Bytes(2, 7)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Append_AtProviderLimit_Succeeds()
    {
        Assert.SkipUnless(MaxAppendBytes is not null, "The provider enforces no per-append limit of its own.");
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(16, 1), CancellationToken);
        var segment = LargeSequence(MaxAppendBytes!.Value, 2);

        await writer.AppendAsync(segment, CancellationToken);

        var expected = new HashingConsumer();
        expected.Add(Bytes(16, 1));
        foreach (var memory in segment)
        {
            expected.Add(memory.Span);
        }

        var actual = new HashingConsumer();
        await CreateStorage(journalId).ReadAsync(actual, CancellationToken);
        Assert.True(actual.IsCompleted);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.GetHash(), actual.GetHash());
    }

    [Fact]
    public async Task Append_OverProviderLimit_ThrowsAndLeavesStorageUnchanged()
    {
        Assert.SkipUnless(MaxAppendBytes is not null, "The provider enforces no per-append limit of its own.");
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(16, 1), CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(
            () => writer.AppendAsync(LargeSequence(MaxAppendBytes!.Value + 1, 2), CancellationToken).AsTask());

        Assert.Equal(Bytes(16, 1), await ReadContentAsync(CreateStorage(journalId)));
        await writer.AppendAsync(Sequence(8, 3), CancellationToken);
        Assert.Equal(Concat([Bytes(16, 1), Bytes(8, 3)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Write_AfterAnotherWriterAppended_ThrowsAndLeavesStorageUnchanged()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(4, 1), CancellationToken);
        var other = CreateStorage(journalId);
        await ReadContentAsync(other);
        await other.AppendAsync(Sequence(5, 2), CancellationToken);

        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.AppendAsync(Sequence(6, 3), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.ReplaceAsync(Sequence(6, 3), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.DeleteAsync(CancellationToken).AsTask());

        Assert.Equal(Concat([Bytes(4, 1), Bytes(5, 2)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Write_AfterAnotherWriterDeleted_ThrowsAndKeepsPrecondition()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(4, 1), CancellationToken);
        var other = CreateStorage(journalId);
        await ReadContentAsync(other);
        await other.DeleteAsync(CancellationToken);

        // 每次遭拒的写入都会保留写入方原有的前置条件，因此后续每次写入也会被拒绝。
        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.DeleteAsync(CancellationToken).AsTask());
        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.ReplaceAsync(Sequence(5, 2), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.AppendAsync(Sequence(6, 3), CancellationToken).AsTask());

        Assert.Empty(await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Write_AfterObservingAbsenceAndAnotherWriterCreated_Throws()
    {
        var journalId = CreateJournalId();
        var appender = CreateStorage(journalId);
        var replacer = CreateStorage(journalId);
        var deleter = CreateStorage(journalId);
        Assert.Empty(await ReadContentAsync(appender));
        Assert.Empty(await ReadContentAsync(replacer));
        Assert.Empty(await ReadContentAsync(deleter));
        await (await CreateWriterAsync(journalId)).AppendAsync(Sequence(5, 1), CancellationToken);

        await Assert.ThrowsAsync<InconsistentStateException>(() => appender.AppendAsync(Sequence(6, 2), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InconsistentStateException>(() => replacer.ReplaceAsync(Sequence(6, 2), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InconsistentStateException>(() => deleter.DeleteAsync(CancellationToken).AsTask());

        Assert.Equal(Bytes(5, 1), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Write_AfterOwnDeleteAndAnotherWriterCreated_Throws()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(5, 1), CancellationToken);
        await writer.DeleteAsync(CancellationToken);
        await (await CreateWriterAsync(journalId)).AppendAsync(Sequence(6, 2), CancellationToken);

        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.AppendAsync(Sequence(7, 3), CancellationToken).AsTask());

        Assert.Equal(Bytes(6, 2), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Write_AfterObservingAbsence_CreatesJournal()
    {
        var journalId = CreateJournalId();
        var appender = CreateStorage(journalId);
        Assert.Empty(await ReadContentAsync(appender));
        await appender.AppendAsync(Sequence(5, 1), CancellationToken);
        await appender.AppendAsync(Sequence(6, 2), CancellationToken);
        Assert.Equal(Concat([Bytes(5, 1), Bytes(6, 2)]), await ReadContentAsync(CreateStorage(journalId)));

        var replacer = CreateStorage(CreateJournalId());
        Assert.Empty(await ReadContentAsync(replacer));
        await replacer.ReplaceAsync(Sequence(7, 3), CancellationToken);
        await replacer.AppendAsync(Sequence(8, 4), CancellationToken);
        Assert.Equal(Concat([Bytes(7, 3), Bytes(8, 4)]), await ReadContentAsync(replacer));
    }

    [Fact]
    public async Task Write_BeforeReadOrCreate_ThrowsAndLeavesStorageUnchanged()
    {
        var journalId = CreateJournalId();
        var absent = CreateStorage(journalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => absent.AppendAsync(Sequence(4, 1), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => absent.ReplaceAsync(Sequence(4, 1), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => absent.DeleteAsync(CancellationToken).AsTask());
        Assert.Null(await CreateStorage(journalId).GetMetadataAsync(CancellationToken));

        await (await CreateWriterAsync(journalId)).AppendAsync(Sequence(5, 1), CancellationToken);
        var writer = CreateStorage(journalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendAsync(Sequence(6, 2), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ReplaceAsync(Sequence(6, 2), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.DeleteAsync(CancellationToken).AsTask());

        Assert.Equal(Bytes(5, 1), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task CreateIfNotExists_WhenJournalExists_ReturnsFalseAndEstablishesNoPrecondition()
    {
        var journalId = CreateJournalId();
        await (await CreateWriterAsync(journalId)).AppendAsync(Sequence(5, 1), CancellationToken);
        var storage = CreateStorage(journalId);

        Assert.False(await storage.CreateIfNotExistsAsync(cancellationToken: CancellationToken));
        Assert.NotNull(await storage.GetMetadataAsync(CancellationToken));
        Assert.NotNull(await storage.UpdateMetadataAsync(new Dictionary<string, string> { ["conformance"] = "1" }, cancellationToken: CancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.AppendAsync(Sequence(6, 2), CancellationToken).AsTask());
        Assert.Equal(Bytes(5, 1), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task ConcurrentWriters_FromOneVersion_ExactlyOneWinsAndNoPartialContentIsVisible()
    {
        const int WriterCount = 4;
        var journalId = CreateJournalId();
        await (await CreateWriterAsync(journalId)).AppendAsync(Sequence(8, 1), CancellationToken);
        var writers = new IJournalStorage[WriterCount];
        for (var i = 0; i < writers.Length; i++)
        {
            writers[i] = CreateStorage(journalId);
            await ReadContentAsync(writers[i]);
        }

        var segments = Enumerable.Range(0, WriterCount).Select(static i => Bytes(10_000 + i, (byte)(10 + i))).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var tasks = writers.Select((writer, i) => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == WriterCount)
            {
                ready.SetResult();
            }

            await start.Task.WaitAsync(CancellationToken);
            return await CaptureAsync(() => writer.AppendAsync(new ReadOnlySequence<byte>(segments[i]), CancellationToken));
        }, CancellationToken)).ToArray();
        await ready.Task.WaitAsync(CancellationToken);
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        var winner = Assert.Single(Enumerable.Range(0, WriterCount), i => results[i] is null);
        Assert.All(results.Where(static exception => exception is not null), static exception => Assert.IsType<InconsistentStateException>(exception));
        Assert.Equal(Concat([Bytes(8, 1), segments[winner]]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task MetadataOperations_DoNotMoveEstablishedPrecondition()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(4, 1), CancellationToken);
        var other = CreateStorage(journalId);
        await ReadContentAsync(other);
        await other.AppendAsync(Sequence(5, 2), CancellationToken);

        Assert.NotNull(await writer.GetMetadataAsync(CancellationToken));
        Assert.NotNull(await writer.UpdateMetadataAsync(new Dictionary<string, string> { ["conformance"] = "1" }, cancellationToken: CancellationToken));
        Assert.False(await writer.CreateIfNotExistsAsync(cancellationToken: CancellationToken));

        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.AppendAsync(Sequence(6, 3), CancellationToken).AsTask());
        Assert.Equal(Concat([Bytes(4, 1), Bytes(5, 2)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task CreateIfNotExists_WhenCreating_EstablishesPrecondition()
    {
        var journalId = CreateJournalId();
        var writer = CreateStorage(journalId);
        Assert.Empty(await ReadContentAsync(writer));

        Assert.True(await writer.CreateIfNotExistsAsync(cancellationToken: CancellationToken));
        await writer.AppendAsync(Sequence(5, 1), CancellationToken);

        Assert.Equal(Bytes(5, 1), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Read_WhenInitialConsumerFails_LeavesPreconditionUninitialized()
    {
        var journalId = CreateJournalId();
        var storage = CreateStorage(journalId);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => storage.ReadAsync(new RejectingConsumer(), CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.AppendAsync(Sequence(5, 1), CancellationToken).AsTask());

        var other = await CreateWriterAsync(journalId);
        await other.AppendAsync(Sequence(6, 2), CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => storage.ReadAsync(new RejectingConsumer(), CancellationToken).AsTask());
        await other.AppendAsync(Sequence(7, 3), CancellationToken);
        Assert.Equal(Concat([Bytes(6, 2), Bytes(7, 3)]), await ReadContentAsync(storage));
        await storage.AppendAsync(Sequence(8, 4), CancellationToken);
        Assert.Equal(Concat([Bytes(6, 2), Bytes(7, 3), Bytes(8, 4)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Read_AfterOwnWritesAndMetadataUpdates_PreservesWritableContent()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        Assert.Empty(await ReadContentAsync(writer));
        await writer.AppendAsync(Sequence(5, 1), CancellationToken);
        Assert.Equal(Bytes(5, 1), await ReadContentAsync(writer));
        await CreateStorage(journalId).UpdateMetadataAsync(
            new Dictionary<string, string> { ["owner"] = "reader" }, cancellationToken: CancellationToken);
        Assert.Equal(Bytes(5, 1), await ReadContentAsync(writer));
        await writer.ReplaceAsync(Sequence(6, 2), CancellationToken);
        Assert.Equal(Bytes(6, 2), await ReadContentAsync(writer));
        await writer.DeleteAsync(CancellationToken);
        Assert.Empty(await ReadContentAsync(writer));
        await writer.AppendAsync(Sequence(7, 3), CancellationToken);
        Assert.Equal(Bytes(7, 3), await ReadContentAsync(writer));
    }

    [Theory]
    [InlineData("append")]
    [InlineData("replace")]
    [InlineData("delete")]
    public async Task Read_AfterAnotherWriterChangedContent_ThrowsAndKeepsPrecondition(string change)
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await writer.AppendAsync(Sequence(5, 1), CancellationToken);
        var other = await CreateWriterAsync(journalId);
        var expected = Bytes(5, 1);
        switch (change)
        {
            case "append":
                await other.AppendAsync(Sequence(6, 2), CancellationToken);
                expected = Concat([expected, Bytes(6, 2)]);
                break;
            case "replace":
                // 即使字节内容相同，也代表不同的一次发布。
                await other.ReplaceAsync(new ReadOnlySequence<byte>(expected), CancellationToken);
                break;
            case "delete":
                await other.DeleteAsync(CancellationToken);
                expected = [];
                break;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var consumer = new CollectingConsumer();
            await Assert.ThrowsAsync<InconsistentStateException>(() => writer.ReadAsync(consumer, CancellationToken).AsTask());
            Assert.Empty(consumer.Content);
            Assert.False(consumer.IsCompleted);
        }

        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.AppendAsync(Sequence(7, 3), CancellationToken).AsTask());
        var recovered = await CreateWriterAsync(journalId);
        Assert.Equal(expected, await ReadContentAsync(recovered));
        await recovered.AppendAsync(Sequence(8, 4), CancellationToken);
        Assert.Equal(Concat([expected, Bytes(8, 4)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Read_AfterObservedAbsenceAndCompetingCreation_ThrowsAndKeepsPrecondition()
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        await (await CreateWriterAsync(journalId)).AppendAsync(Sequence(5, 1), CancellationToken);

        await Assert.ThrowsAsync<InconsistentStateException>(() => ReadContentAsync(writer));
        await Assert.ThrowsAsync<InconsistentStateException>(() => ReadContentAsync(writer));
        await Assert.ThrowsAsync<InconsistentStateException>(() => writer.AppendAsync(Sequence(6, 2), CancellationToken).AsTask());
        Assert.Equal(Bytes(5, 1), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Append_EmptySegment_ThrowsAndLeavesStorageUnchanged(bool exists)
    {
        var journalId = CreateJournalId();
        var writer = await CreateWriterAsync(journalId);
        if (exists)
        {
            await writer.AppendAsync(Sequence(5, 1), CancellationToken);
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => writer.AppendAsync(ReadOnlySequence<byte>.Empty, CancellationToken).AsTask());
        Assert.Equal(exists, await CreateStorage(journalId).GetMetadataAsync(CancellationToken) is not null);
        var expected = exists ? Bytes(5, 1) : [];
        Assert.Equal(expected, await ReadContentAsync(CreateStorage(journalId)));
        await writer.AppendAsync(Sequence(6, 2), CancellationToken);
        Assert.Equal(Concat([expected, Bytes(6, 2)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Append_WhenEveryRequestBodyIsAborted_FailsAndLeavesStorageUnchanged()
    {
        SkipUnlessSupported(JournalStorageFaultKind.AbortRequestBody);
        var journalId = CreateJournalId();
        var writer = await CreateFaultingWriterAsync(journalId);
        await writer.AppendAsync(Sequence(16, 1), CancellationToken);

        Faults.Arm(JournalStorageFaultKind.AbortRequestBody, count: int.MaxValue);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => writer.AppendAsync(Sequence(50_000, 2), CancellationToken).AsTask());
            Assert.True(Faults.InjectedCount > 0);
        }
        finally
        {
            Faults.Disarm();
        }

        Assert.Equal(Bytes(16, 1), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Append_WhenOneRequestBodyIsAborted_WritesSegmentWholeOrNotAtAll()
    {
        SkipUnlessSupported(JournalStorageFaultKind.AbortRequestBody);
        var journalId = CreateJournalId();
        var writer = await CreateFaultingWriterAsync(journalId);
        await writer.AppendAsync(Sequence(16, 1), CancellationToken);

        Faults.Arm(JournalStorageFaultKind.AbortRequestBody, count: 1);
        var exception = await CaptureAsync(() => writer.AppendAsync(Sequence(50_000, 2), CancellationToken));
        Faults.Disarm();

        Assert.Equal(1, Faults.InjectedCount);
        var expected = exception is null ? Concat([Bytes(16, 1), Bytes(50_000, 2)]) : Bytes(16, 1);
        Assert.Equal(expected, await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task Append_WhenResponseIsLost_WritesWholeSegmentOrNothing()
    {
        SkipUnlessSupported(JournalStorageFaultKind.LoseResponse);
        var journalId = CreateJournalId();
        var writer = await CreateFaultingWriterAsync(journalId);
        await writer.AppendAsync(Sequence(16, 1), CancellationToken);

        Faults.Arm(JournalStorageFaultKind.LoseResponse, count: 1);
        var exception = await CaptureAsync(() => writer.AppendAsync(Sequence(50_000, 2), CancellationToken));
        Faults.Disarm();

        Assert.Equal(1, Faults.InjectedCount);
        var content = await ReadContentAsync(CreateStorage(journalId));
        var whole = Concat([Bytes(16, 1), Bytes(50_000, 2)]);
        if (exception is null)
        {
            Assert.Equal(whole, content);
        }
        else
        {
            Assert.True(content.AsSpan().SequenceEqual(Bytes(16, 1)) || content.AsSpan().SequenceEqual(whole), $"Read {content.Length} bytes after a lost append response.");
        }
    }

    [Fact]
    public async Task Replace_WhenEveryRequestBodyIsAborted_FailsAndLeavesStorageUnchanged()
    {
        SkipUnlessSupported(JournalStorageFaultKind.AbortRequestBody);
        var journalId = CreateJournalId();
        var writer = await CreateFaultingWriterAsync(journalId);
        await writer.AppendAsync(Sequence(16, 1), CancellationToken);
        await writer.AppendAsync(Sequence(17, 2), CancellationToken);

        Faults.Arm(JournalStorageFaultKind.AbortRequestBody, count: int.MaxValue);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => writer.ReplaceAsync(Sequence(200_000, 3), CancellationToken).AsTask());
        }
        finally
        {
            Faults.Disarm();
        }

        Assert.Equal(Concat([Bytes(16, 1), Bytes(17, 2)]), await ReadContentAsync(CreateStorage(journalId)));
    }

    [Fact]
    public async Task RandomOperations_WithInjectedFailures_MatchModel()
    {
        var operation = Gen.OneOf(
            Gen.Byte.Array[1, 300].Select(static data => new ModelOperation(ModelOperationKind.Append, data)),
            Gen.Byte.Array[1, 300].Select(static data => new ModelOperation(ModelOperationKind.Replace, data)),
            Gen.Byte.Array[1, 64].Select(static data => new ModelOperation(ModelOperationKind.ForeignAppend, data)),
            Gen.Const(new ModelOperation(ModelOperationKind.ForeignDelete, [])),
            Gen.Const(new ModelOperation(ModelOperationKind.Read, [])),
            Gen.Const(new ModelOperation(ModelOperationKind.Delete, [])),
            Gen.Byte.Array[1, 64].Select(static data => new ModelOperation(ModelOperationKind.OversizedAppend, data)),
            Gen.Byte.Array[1, 300].Select(static data => new ModelOperation(ModelOperationKind.AbortedAppend, data)),
            Gen.Byte.Array[1, 300].Select(static data => new ModelOperation(ModelOperationKind.LostResponseAppend, data)));

        await operation.Array[1, 12].SampleAsync(
            RunModelAsync,
            iter: PropertyIterations,
            threads: 1,
            print: static operations => string.Join(", ", operations.Select(static operation => $"{operation.Kind}({operation.Data.Length})")));
    }

    // 通过一个写入方对新日志执行一个操作序列，将每个操作的结果及操作后的内容
    // 与日志及写入方前置条件的模型进行比较。
    private async Task RunModelAsync(ModelOperation[] operations)
    {
        var journalId = CreateJournalId();
        var writer = CreateWriter();
        var model = new JournalModel();
        Assert.Empty(await ReadContentAsync(writer));
        model.Read(exists: false);

        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case ModelOperationKind.Append:
                    await ExpectWriteAsync(() => writer.AppendAsync(new ReadOnlySequence<byte>(operation.Data), CancellationToken), () => model.Append(operation.Data));
                    break;
                case ModelOperationKind.Replace:
                    await ExpectWriteAsync(() => writer.ReplaceAsync(new ReadOnlySequence<byte>(operation.Data), CancellationToken), () => model.Replace(operation.Data));
                    break;
                case ModelOperationKind.Delete:
                    await ExpectWriteAsync(() => writer.DeleteAsync(CancellationToken), model.Delete);
                    break;
                case ModelOperationKind.ForeignAppend:
                    {
                        var other = CreateStorage(journalId);
                        await ReadContentAsync(other);
                        await other.AppendAsync(new ReadOnlySequence<byte>(operation.Data), CancellationToken);
                        model.ForeignAppend(operation.Data);
                        break;
                    }

                case ModelOperationKind.ForeignDelete:
                    {
                        var other = CreateStorage(journalId);
                        await ReadContentAsync(other);
                        await other.DeleteAsync(CancellationToken);
                        model.ForeignDelete();
                        break;
                    }

                case ModelOperationKind.Read:
                    if (model.IsWriterStale)
                    {
                        await Assert.ThrowsAsync<InconsistentStateException>(() => ReadContentAsync(writer));
                        writer = CreateWriter();
                    }

                    Assert.Equal(model.Content, await ReadContentAsync(writer));
                    model.Read(exists: await writer.GetMetadataAsync(CancellationToken) is not null);
                    break;
                case ModelOperationKind.OversizedAppend when MaxAppendBytes is { } maxAppendBytes:
                    {
                        var oversized = LargeSequence(maxAppendBytes + 1, operation.Data[0]);
                        await Assert.ThrowsAnyAsync<Exception>(() => writer.AppendAsync(oversized, CancellationToken).AsTask());
                        break;
                    }

                case ModelOperationKind.AbortedAppend when SupportedFaults.Contains(JournalStorageFaultKind.AbortRequestBody):
                    {
                        Faults.Arm(JournalStorageFaultKind.AbortRequestBody, count: int.MaxValue);
                        Exception? exception;
                        try
                        {
                            exception = await CaptureAsync(() => writer.AppendAsync(new ReadOnlySequence<byte>(operation.Data), CancellationToken));
                        }
                        finally
                        {
                            Faults.Disarm();
                        }

                        Assert.NotNull(exception);
                        await RecoverAfterFailureAsync();
                        break;
                    }

                case ModelOperationKind.LostResponseAppend when SupportedFaults.Contains(JournalStorageFaultKind.LoseResponse):
                    {
                        Faults.Arm(JournalStorageFaultKind.LoseResponse, count: 1);
                        var exception = await CaptureAsync(() => writer.AppendAsync(new ReadOnlySequence<byte>(operation.Data), CancellationToken));
                        Faults.Disarm();
                        if (exception is null)
                        {
                            Assert.False(model.IsWriterStale);
                            model.Append(operation.Data);
                            break;
                        }

                        // 结果不确定：该段要么完整写入，要么完全未写入。
                        var content = await ReadContentAsync(CreateStorage(journalId));
                        var withSegment = Concat([model.Content, operation.Data]);
                        var applied = !model.IsWriterStale && content.AsSpan().SequenceEqual(withSegment);
                        Assert.True(
                            applied || content.AsSpan().SequenceEqual(model.Content),
                            $"Read {content.Length} bytes after a lost append response; expected {model.Content.Length} or {withSegment.Length}.");
                        if (applied)
                        {
                            model.ForeignAppend(operation.Data);
                        }

                        await RecoverAfterFailureAsync();
                        break;
                    }

                default:
                    // 提供程序既没有单次追加限制，也没有故障注入，因此此次操作是普通的追加操作。
                    await ExpectWriteAsync(() => writer.AppendAsync(new ReadOnlySequence<byte>(operation.Data), CancellationToken), () => model.Append(operation.Data));
                    break;
            }

            Assert.Equal(model.Content, await ReadContentAsync(CreateStorage(journalId)));
        }

        IJournalStorage CreateWriter() => SupportsFaultInjection ? CreateFaultingStorage(journalId) : CreateStorage(journalId);

        async Task ExpectWriteAsync(Func<ValueTask> write, Action apply)
        {
            if (model.IsWriterStale)
            {
                await Assert.ThrowsAsync<InconsistentStateException>(() => write().AsTask());
            }
            else
            {
                await write();
                apply();
            }
        }

        // 日志化状态管理器在写入失败后通过重新读取来恢复，因此模型也如此处理。该读取操作还会
        // 显示失败的写入是否留下了空日志。
        async Task RecoverAfterFailureAsync()
        {
            writer = CreateWriter();
            Assert.Equal(model.Content, await ReadContentAsync(writer));
            model.Read(exists: await writer.GetMetadataAsync(CancellationToken) is not null);
        }
    }

    private void SkipUnlessSupported(JournalStorageFaultKind fault)
        => Assert.SkipUnless(SupportedFaults.Contains(fault), $"The provider's conformance tests do not inject {fault} faults.");

    protected IJournalStorage CreateStorage(JournalId journalId)
        => (_provider ?? throw new InvalidOperationException("The test has not been initialized.")).CreateStorage(journalId);

    protected IJournalStorage CreateFaultingStorage(JournalId journalId)
        => (_faultingProvider ?? throw new InvalidOperationException("The provider does not support fault injection.")).CreateStorage(journalId);

    // 写入方在写入前读取日志，就像日志化状态管理器恢复时所做的那样。
    protected async Task<IJournalStorage> CreateWriterAsync(JournalId journalId)
    {
        var storage = CreateStorage(journalId);
        await ReadContentAsync(storage);
        return storage;
    }

    protected async Task<IJournalStorage> CreateFaultingWriterAsync(JournalId journalId)
    {
        var storage = CreateFaultingStorage(journalId);
        await ReadContentAsync(storage);
        return storage;
    }

    protected virtual JournalId CreateJournalId() => new($"conformance/{Guid.NewGuid():N}");

    protected static async Task<byte[]> ReadContentAsync(IJournalStorage storage)
    {
        var consumer = new CollectingConsumer();
        await storage.ReadAsync(consumer, CancellationToken);
        Assert.True(consumer.IsCompleted);
        return consumer.Content.ToArray();
    }

    private static async Task<Exception?> CaptureAsync(Func<ValueTask> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static byte[] Bytes(int length, byte seed)
    {
        var result = new byte[length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = unchecked((byte)(seed + i * 31));
        }

        return result;
    }

    private static ReadOnlySequence<byte> Sequence(int length, byte seed) => new(Bytes(length, seed));

    // 使用同一个共享数据块构造指定长度的序列，因此达到大小上限的段无需同等大小的缓冲区。
    private static ReadOnlySequence<byte> LargeSequence(long length, byte seed)
    {
        var chunk = Bytes(1024 * 1024, seed);
        var first = new SequenceSegment(chunk.AsMemory(0, (int)Math.Min(chunk.Length, length)));
        var last = first;
        for (var remaining = length - last.Memory.Length; remaining > 0; remaining -= last.Memory.Length)
        {
            last = last.Append(chunk.AsMemory(0, (int)Math.Min(chunk.Length, remaining)));
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static byte[] Concat(IEnumerable<byte[]> segments) => segments.SelectMany(static segment => segment).ToArray();

    private enum ModelOperationKind
    {
        Append,
        Replace,
        Delete,
        ForeignAppend,
        ForeignDelete,
        Read,
        OversizedAppend,
        AbortedAppend,
        LostResponseAppend,
    }

    private sealed record ModelOperation(ModelOperationKind Kind, byte[] Data);

    // 对日志的内容和版本，以及写入方前置条件中持有的版本进行建模。对于不存在的日志，版本按
    // 值比较，与提供程序处理前置条件的方式相同。
    private sealed class JournalModel
    {
        private long _version;
        private bool _exists;
        private (bool Known, bool Exists, long Version) _writer;

        public byte[] Content { get; private set; } = [];

        public bool IsWriterStale => _writer.Known && (_writer.Exists != _exists || (_exists && _writer.Version != _version));

        public void Append(byte[] data) => WriterWrote(() => Content = Concat([Content, data]), exists: true);

        public void Replace(byte[] data) => WriterWrote(() => Content = data, exists: true);

        public void Delete() => WriterWrote(() => Content = [], exists: false);

        public void ForeignAppend(byte[] data) => Change(() => Content = Concat([Content, data]), exists: true);

        public void ForeignDelete() => Change(() => Content = [], exists: false);

        public void Read(bool exists)
        {
            if (exists != _exists)
            {
                Change(static () => { }, exists);
            }

            _writer = (true, _exists, _version);
        }

        private void WriterWrote(Action change, bool exists)
        {
            Change(change, exists);
            _writer = (true, _exists, _version);
        }

        private void Change(Action change, bool exists)
        {
            change();
            _exists = exists;
            _version++;
        }
    }

    private sealed class RejectingConsumer : IJournalStorageConsumer
    {
        public void Read(JournalBufferReader buffer, IJournalMetadata? metadata)
            => throw new InvalidDataException("Injected recovery failure.");
    }

    private sealed class CollectingConsumer : IJournalStorageConsumer
    {
        public List<byte> Content { get; } = [];

        public bool IsCompleted { get; private set; }

        public void Read(JournalBufferReader buffer, IJournalMetadata? metadata)
        {
            Assert.False(IsCompleted);
            Content.AddRange(buffer.ToArray());
            buffer.Skip(buffer.Length);
            IsCompleted = buffer.IsCompleted;
        }
    }

    private sealed class HashingConsumer : IJournalStorageConsumer
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long Length { get; private set; }

        public bool IsCompleted { get; private set; }

        public void Add(ReadOnlySpan<byte> data)
        {
            _hash.AppendData(data);
            Length += data.Length;
        }

        public byte[] GetHash() => _hash.GetHashAndReset();

        public void Read(JournalBufferReader buffer, IJournalMetadata? metadata)
        {
            Add(buffer.ToArray());
            buffer.Skip(buffer.Length);
            IsCompleted = buffer.IsCompleted;
        }
    }

    private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public SequenceSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public SequenceSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new SequenceSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}

/// <summary>
/// <see cref="JournalStorageFaultInjector"/> 向提供程序的写入请求注入的传输故障。
/// </summary>
public enum JournalStorageFaultKind
{
    /// <summary>
    /// 请求正文发送到一半时失败，因此存储端只收到部分请求。
    /// </summary>
    AbortRequestBody,

    /// <summary>
    /// 存储端应用请求，但随后客户端丢失响应。
    /// </summary>
    LoseResponse,
}

/// <summary>
/// 向提供程序的写入请求注入传输故障。
/// </summary>
public sealed class JournalStorageFaultInjector
{
    private readonly object _lock = new();
    private JournalStorageFaultKind _kind;
    private int _remaining;
    private int _injectedCount;

    /// <summary>
    /// 获取自注入器上次启用以来注入的故障数量。
    /// </summary>
    public int InjectedCount
    {
        get
        {
            lock (_lock)
            {
                return _injectedCount;
            }
        }
    }

    /// <summary>
    /// 将 <paramref name="kind"/> 类型的故障注入接下来的 <paramref name="count"/> 个能够触发该故障的写入请求。
    /// </summary>
    public void Arm(JournalStorageFaultKind kind, int count)
    {
        lock (_lock)
        {
            _kind = kind;
            _remaining = count;
            _injectedCount = 0;
        }
    }

    /// <summary>
    /// 停止注入故障。
    /// </summary>
    public void Disarm()
    {
        lock (_lock)
        {
            _remaining = 0;
        }
    }

    /// <summary>
    /// 如果还有已启用但尚未注入的 <paramref name="kind"/> 类型故障，则取出一个。
    /// </summary>
    public bool TryTake(JournalStorageFaultKind kind)
    {
        lock (_lock)
        {
            if (_remaining == 0 || _kind != kind)
            {
                return false;
            }

            _remaining--;
            _injectedCount++;
            return true;
        }
    }
}
