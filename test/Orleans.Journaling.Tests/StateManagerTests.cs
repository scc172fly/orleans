using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Buffers.Adaptors;
using Orleans.Serialization.Session;
using Orleans.Storage;
using NSubstitute;
using Xunit;

namespace Orleans.Durable.Journaling.Tests;

/// <summary>
/// 状态管理器的测试。状态管理器是 Orleans 日志化基础设施的核心组件。
/// 
/// 状态管理器在单个 Grain 内协调多个持久化数据结构（DurableDictionary、DurableList 等），
/// 确保所有状态变更以原子方式记入日志，并可以
/// 一同恢复。它管理状态的生命周期，通过
/// WriteStateAsync 调用处理持久化，并确保故障后的恢复保持一致。
/// </summary>
[TestSuite("BVT")]
[TestProvider("None")]
[TestCategory("BVT")]
public partial class StateManagerTests : JournalingTestBase
{
    /// <summary>
    /// 测试多个状态的注册和基本操作。
    /// 验证不同类型的持久化集合能否注册到
    /// 管理器并独立运行。
    /// </summary>
    [Fact]
    public async Task StateManager_RegisterState_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var codec = CodecProvider.GetCodec<int>();

        // 执行 - 注册状态
        var dictionary = new DurableDictionary<string, int>("dict1", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), codec, SessionPool));
        var list = new DurableList<string>("list1", manager, new OrleansBinaryDurableListCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool));
        var queue = new DurableQueue<int>("queue1", manager, new OrleansBinaryDurableQueueCommandCodec<int>(codec, SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 添加一些数据
        dictionary.Add("key1", 1);
        list.Add("item1");
        queue.Enqueue(42);

        // 写入状态
        await manager.WriteStateAsync(CancellationToken.None);

        // 断言 - 数据已正确存储
        Assert.Equal(1, dictionary["key1"]);
        Assert.Equal("item1", list[0]);
        Assert.Equal(42, queue.Peek());
    }

    [Fact]
    public async Task StateManager_Initialize_UsesStreamingStorageRead()
    {
        var storage = new StreamingOnlyStorage();
        var sut = CreateTestSystem(storage: storage);

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.True(storage.StreamingReadCalled);
    }

    [Fact]
    public async Task StateManager_Initialize_ThrowsWhenCompletedReadLeavesData()
    {
        var storage = await new VolatileJournalStorage().ReadForWriteAsync();
        using (var data = CreateBuffer([1, 2, 3]))
        {
            await storage.AppendAsync(data.AsReadOnlySequence(), CancellationToken.None);
        }

        var sut = CreateTestSystem(storage: storage, journalFormat: new NonConsumingJournalFormat());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.NotNull(exception.InnerException);
        Assert.Contains("did not read the completed journal data", exception.InnerException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateManager_WriteOperations_RequireInitialization()
    {
        var sut = CreateTestSystem();

        var writeException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.WriteStateAsync(CancellationToken.None).AsTask());
        var deleteException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.DeleteStateAsync(CancellationToken.None).AsTask());

        Assert.Contains("not been initialized", writeException.Message, StringComparison.Ordinal);
        Assert.Contains("not been initialized", deleteException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateManager_WriteOperations_RequireSuccessfulInitialization()
    {
        var storage = new CapturingStorage
        {
            NextReadException = new IOException("Expected recovery failure.")
        };
        var sut = CreateTestSystem(storage: storage);

        await Assert.ThrowsAsync<IOException>(() => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken));

        var writeException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        var deleteException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("not been initialized", writeException.Message, StringComparison.Ordinal);
        Assert.Contains("not been initialized", deleteException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateManager_WriteOperations_ObserveShutdown()
    {
        var sut = CreateTestSystem();
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        await sut.Lifecycle.OnStop(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task StateManager_Recovery_PreservesUnknownStateEntry()
    {
        var storage = await new VolatileJournalStorage().ReadForWriteAsync();
        using var segment = new OrleansBinaryJournalBufferWriter();
        using (var entry = segment.CreateJournalStreamWriter(new JournalStreamId(99)).BeginEntry())
        {
            entry.Writer.Write(new byte[] { 1, 2, 3 });
            entry.Commit();
        }

        using var data = segment.GetBuffer();
        var originalBytes = data.ToArray();
        await storage.AppendAsync(data.AsReadOnlySequence(), CancellationToken.None);
        var sut = CreateTestSystem(storage: storage);

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var segmentBytes = Assert.Single(storage.Segments);
        Assert.Equal(originalBytes, segmentBytes);
    }

    [Fact]
    public async Task StateManager_UnknownStateCompaction_PreservesDecodedPayloadThroughFormatWriter()
    {
        var physicalBytes = new byte[] { 0xF0, 0x0D, 0x99 };
        var decodedPayload = new byte[] { 1, 2, 3 };
        var storage = new CapturingStorage { IsCompactionRequested = true };
        using (var data = CreateBuffer(physicalBytes))
        {
            await storage.AppendAsync(data.AsReadOnlySequence(), CancellationToken.None);
        }

        var format = new DecodedPayloadOnlyJournalFormat(new JournalStreamId(99), decodedPayload, SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var replacement = Assert.Single(storage.Replaces);
        Assert.NotEqual(physicalBytes, replacement);

        var entries = ReadBinaryEntries(replacement);
        var preserved = Assert.Single(entries, entry => entry.StreamId.Value == 99);
        Assert.Equal(decodedPayload, preserved.Payload);

        Assert.Contains(format.Writers, writer => writer.BeganEntryIds.Contains(99u));
    }

    [Fact]
    public async Task StateManager_RetiredStateCompaction_WritesPreservedPayloadThroughFormatOwnedEntry()
    {
        var storage = new CapturingStorage();
        var initial = CreateTestSystem(storage: storage);
        var dictionary = new DurableDictionary<string, int>("retired", initial.Manager, CreateDictionaryCodec<string, int>());

        await initial.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("key", 7);
        await initial.Manager.WriteStateAsync(CancellationToken.None);

        storage.IsCompactionRequested = true;
        var format = new TrackingJournalFormat(SessionPool);
        var compacting = CreateTestSystem(storage: storage, journalFormat: format);

        await compacting.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        await compacting.Manager.WriteStateAsync(CancellationToken.None);

        Assert.Contains(format.Writers, writer => writer.BeganEntryIds.Any(id => id >= 8));
        Assert.Single(storage.Replaces);

        var recovered = CreateTestSystem(storage: storage);
        var recoveredDictionary = new DurableDictionary<string, int>("retired", recovered.Manager, CreateDictionaryCodec<string, int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(7, recoveredDictionary["key"]);
    }

    [Fact]
    public async Task StateManager_DirectWrites_UseFormatOwnedCurrentSegmentWriter()
    {
        var storage = new CapturingStorage();
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("key", 1);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var writer = Assert.Single(format.Writers);
        Assert.Single(storage.Appends);
        Assert.Empty(storage.Replaces);
        Assert.Contains(0u, writer.BeganEntryIds);
        Assert.Contains(writer.BeganEntryIds, id => id >= 8);
    }

    [Fact]
    public async Task StateManager_AppendJournalFlush_UsesFormatOwnedWriter()
    {
        var storage = new CapturingStorage();
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var writer = Assert.Single(format.Writers);
        Assert.Single(storage.Appends);
        Assert.Contains(writer.BeganEntryIds, id => id >= 8);
        Assert.True(storage.Appends[0].Length > 0);
    }

    [Fact]
    public async Task StateManager_BinaryAppend_StoresBinaryVarUIntEntries()
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("key", 1);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var append = Assert.Single(storage.Appends);
        Assert.Empty(storage.Replaces);
        AssertContainsRuntimeAndApplicationEntries(ReadBinaryEntries(append));
    }

    [Fact]
    public async Task StateManager_AppendBufferIsBorrowedUntilStorageCompletes()
    {
        var storage = new DelayedBorrowingStorage();
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        Assert.NotNull(storage.AppendBytesAfterYield);
        Assert.NotEmpty(storage.AppendBytesAfterYield);
    }

    [Fact]
    public async Task StateManager_ReplaceBufferIsBorrowedUntilStorageCompletes()
    {
        var storage = new DelayedBorrowingStorage { IsCompactionRequested = true };
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        Assert.NotNull(storage.ReplaceBytesAfterYield);
        Assert.NotEmpty(storage.ReplaceBytesAfterYield);
    }

    [Fact]
    public async Task StateManager_BinarySnapshot_StoresBinaryVarUIntEntries()
    {
        var storage = new CapturingStorage { IsCompactionRequested = true };
        var sut = CreateTestSystem(storage: storage);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("key", 1);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var replacement = Assert.Single(storage.Replaces);
        Assert.Empty(storage.Appends);
        AssertContainsRuntimeAndApplicationEntries(ReadBinaryEntries(replacement));
    }

    [Fact]
    public async Task StateManager_StorageOperationBytesMetric_RecordsWritePayloadSizes()
    {
        var observedBytes = new ConcurrentBag<MetricMeasurement<long>>();
        using var listener = CreateMetricListener("orleans-journaling-storage-operation-bytes", observedBytes);
        var storage = new CapturingStorage { IsCompactionRequested = true };
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var replacement = Assert.Single(storage.Replaces);
        Assert.Empty(storage.Appends);
        Assert.Contains(observedBytes, measurement => measurement.Operation == "replace" && measurement.Status == "ok" && measurement.Value == replacement.Length);
    }

    [Fact]
    public void JournalingInstruments_StorageOperationBytesMetric_SkipsZeroByteAndFailedOperations()
    {
        var observedBytes = new ConcurrentBag<MetricMeasurement<long>>();
        using var listener = CreateMetricListener("orleans-journaling-storage-operation-bytes", observedBytes);
        var operation = $"test-{Guid.NewGuid():N}";
        var instruments = JournalingInstruments.CreateForDirectConstruction();

        instruments.OnStorageOperation(operation, TimeSpan.Zero, bytes: 0, succeeded: true);
        instruments.OnStorageOperation(operation, TimeSpan.Zero, bytes: 1, succeeded: false);

        Assert.DoesNotContain(observedBytes, measurement => measurement.Operation == operation);
    }

    [Fact]
    public async Task StateManager_StorageOperationQueueDurationMetric_RecordsWaitBehindPreviousAppend()
    {
        var observedDurations = new ConcurrentBag<MetricMeasurement<double>>();
        using var listener = CreateMetricListener("orleans-journaling-storage-operation-queue-duration", observedDurations);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var storage = new BlockingAppendStorage();
        var sut = CreateTestSystem(storage: storage, provider: timeProvider);
        var state = new AlwaysWritingState();
        sut.Manager.RegisterStateMachine("state", state);

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        var firstWrite = sut.Manager.WriteStateAsync(CancellationToken.None).AsTask();
        await storage.FirstAppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var secondWrite = sut.Manager.WriteStateAsync(CancellationToken.None).AsTask();
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        storage.AllowFirstAppend.SetResult();

        await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(2, storage.Appends.Count);
        Assert.Contains(observedDurations, measurement => measurement.Operation == "append" && measurement.Status == "ok" && measurement.Value >= 250);
    }

    [Fact]
    public async Task StateManager_DeleteState_ReallocatesApplicationStreamsAboveInternalRange()
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("before", 1);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        await sut.Manager.DeleteStateAsync(CancellationToken.None);
        dictionary.Add("after", 2);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        Assert.Equal(2, storage.Appends.Count);
        var entries = ReadBinaryEntries(storage.Appends[^1]);
        Assert.DoesNotContain(entries, entry => entry.StreamId.Value == 1);
        Assert.Contains(entries, entry => entry.StreamId.Value >= 8);

        var recovered = CreateTestSystem(storage: storage);
        var recoveredDictionary = new DurableDictionary<string, int>("dict", recovered.Manager, CreateDictionaryCodec<string, int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Single(recoveredDictionary);
        Assert.Equal(2, recoveredDictionary["after"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StateManager_DeleteState_DiscardsPendingEntriesBeforeNextAppend(bool persistBeforeDelete)
    {
        var storage = new CapturingStorage();
        await using var manager = CreateTestSystem(storage).Manager;
        var listCodec = new OrleansBinaryDurableListCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool);
        var list = new DurableList<string>("b", manager, listCodec);
        var value = new DurableValue<string>("a", manager, CreateValueCodec<string>());
        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        if (persistBeforeDelete)
        {
            list.Add("persisted-item");
            await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        }

        list.Add("deleted-item");
        value.Value = "before";
        await manager.DeleteStateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(list);
        Assert.Null(value.Value);
        Assert.Empty(storage.RecoverableBytes);

        value.Value = "after";
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);

        // 当后续目录条目重置状态时，恢复过程可能掩盖陈旧的条目。
        var entries = ReadBinaryEntries(storage.RecoverableBytes);
        Assert.Single(entries, entry => entry.StreamId.Value >= 8);
        var replay = ReplayValueCommands(storage.RecoverableBytes);
        Assert.Equal(["after"], replay.AppliedValues);

        await using var recovered = CreateTestSystem(storage).Manager;
        var recoveredList = new DurableList<string>("b", recovered, listCodec);
        var recoveredValue = new DurableValue<string>("a", recovered, CreateValueCodec<string>());
        await recovered.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Empty(recoveredList);
        Assert.Equal("after", recoveredValue.Value);
    }

    [Fact]
    public async Task StateManager_DeleteFailure_PreservesPendingEntriesAndFencesManager()
    {
        var expected = new IOException("Expected deletion failure.");
        var storage = new CapturingStorage { NextDeleteException = expected };
        await using var manager = CreateTestSystem(storage).Manager;
        var dictionary = new DurableDictionary<string, int>("dict", manager, CreateDictionaryCodec<string, int>());
        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        dictionary.Add("pending", 42);
        var pendingBytes = manager.PendingWriteByteCount;
        Assert.True(pendingBytes > 0);

        var exception = await Assert.ThrowsAsync<IOException>(
            () => manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, exception);
        Assert.Equal(pendingBytes, manager.PendingWriteByteCount);
        Assert.Equal(42, Assert.Single(dictionary).Value);
        var fenced = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Same(expected, fenced.InnerException);
    }

    [Fact]
    public async Task StateManager_DeleteState_ClearsDurableValueDirtyFlag()
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 1;
        await sut.Manager.WriteStateAsync(CancellationToken.None);
        value.Value = 2;

        await sut.Manager.DeleteStateAsync(CancellationToken.None);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        Assert.Equal(2, storage.Appends.Count);
        var postDeleteEntries = ReadBinaryEntries(storage.Appends[^1]);
        Assert.Contains(postDeleteEntries, entry => entry.StreamId.Value == 0);
        Assert.DoesNotContain(postDeleteEntries, entry => entry.StreamId.Value >= 8);
    }

    [Fact]
    public async Task StateManager_SnapshotFlush_ResetsPendingAppendData()
    {
        var storage = new CapturingStorage { IsCompactionRequested = true };
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("key", 1);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        Assert.Empty(storage.Appends);
        Assert.Single(storage.Replaces);
        Assert.Equal(0, sut.Manager.PendingWriteByteCount);

        var recovered = CreateTestSystem(storage: storage, journalFormat: new TrackingJournalFormat(SessionPool));
        var recoveredDictionary = new DurableDictionary<string, int>("dict", recovered.Manager, CreateDictionaryCodec<string, int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(1, recoveredDictionary["key"]);
    }

    [Fact]
    public async Task StateManager_SnapshotFailure_FencesMixedPendingMutations()
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());
        var notifications = new AlwaysWritingState();
        sut.Manager.RegisterStateMachine("notifications", notifications);

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("persisted", 1);
        value.Value = 1;
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, notifications.WriteCompletedCount);
        var baselineRecoverableBytes = storage.RecoverableBytes;
        var notificationCountBeforeFailure = notifications.WriteCompletedCount;
        storage.OperationLog.Clear();

        storage.IsCompactionRequested = true;
        dictionary.Add("pending", 2);
        value.Value = 2;
        var pendingBytes = sut.Manager.PendingWriteByteCount;
        var expected = new IOException("Expected replace failure.");
        storage.NextReplaceException = expected;

        var exception = await Assert.ThrowsAsync<IOException>(
            () => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, exception);
        Assert.Equal(["replace-failed"], storage.OperationLog);
        Assert.Equal(baselineRecoverableBytes, storage.RecoverableBytes);
        var failedAttemptBytes = Assert.Single(storage.FailedReplaceAttempts);
        Assert.NotEmpty(failedAttemptBytes);
        Assert.Single(storage.Appends);
        Assert.Empty(storage.Replaces);
        Assert.Equal(1, notifications.WriteCompletedCount);
        Assert.Equal(notificationCountBeforeFailure, notifications.WriteCompletedCount);
        Assert.Equal(pendingBytes, sut.Manager.PendingWriteByteCount);
        Assert.Equal(2, dictionary["pending"]);
        Assert.Equal(2, value.Value);

        var fenced = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Same(expected, fenced.InnerException);
        Assert.Empty(storage.Replaces);
        Assert.Equal(1, storage.ReplaceAttemptCount);
        Assert.Equal(notificationCountBeforeFailure, notifications.WriteCompletedCount);

        var recovered = CreateTestSystem(storage: storage);
        var recoveredDictionary = new DurableDictionary<string, int>("dict", recovered.Manager, CreateDictionaryCodec<string, int>());
        var recoveredValue = new DurableValue<int>("value", recovered.Manager, CreateValueCodec<int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(1, recoveredDictionary["persisted"]);
        Assert.False(recoveredDictionary.ContainsKey("pending"));
        Assert.Equal(1, recoveredValue.Value);
    }

    [Fact]
    public async Task StateManager_DirectWriteFailure_AbortsEntryBeforeMutation()
    {
        var storage = new CapturingStorage();
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var dictionary = new DurableDictionary<int, int>("dict", sut.Manager, new ThrowingDictionarySetCodec<int, int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        var writer = Assert.Single(format.Writers);
        var lengthBefore = GetCommittedLength(writer);

        Assert.Throws<InvalidOperationException>(() => dictionary.Add(1, 1));

        Assert.Empty(dictionary);
        Assert.Equal(lengthBefore, GetCommittedLength(writer));

        await sut.Manager.WriteStateAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StateManager_DirectWriteFailure_DoesNotPersistPartialEntry()
    {
        var storage = new CapturingStorage();
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var codec = new ToggleThrowingDictionarySetCodec<int, int>(CreateDictionaryCodec<int, int>());
        var dictionary = new DurableDictionary<int, int>("dict", sut.Manager, codec);

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        codec.ThrowOnSet = true;
        Assert.Throws<InvalidOperationException>(() => dictionary.Add(1, 1));

        codec.ThrowOnSet = false;
        dictionary.Add(1, 1);
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        var recovered = CreateTestSystem(storage: storage, journalFormat: new TrackingJournalFormat(SessionPool));
        var recoveredDictionary = new DurableDictionary<int, int>("dict", recovered.Manager, CreateDictionaryCodec<int, int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Single(recoveredDictionary);
        Assert.Equal(1, recoveredDictionary[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StateManager_WriteFailurePreservesInterleavedStateAndFencesAllCallers(bool conflict)
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var dictionary = new DurableDictionary<string, int>("dict", sut.Manager, CreateDictionaryCodec<string, int>());
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        dictionary.Add("persisted", 1);
        value.Value = 1;
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        storage.ResetReadConsumeCount();

        Exception expected = conflict
            ? new InconsistentStateException("Expected storage conflict.")
            : new IOException("Expected storage failure.");
        storage.NextAppendException = expected;
        storage.BlockNextAppend = true;
        value.Value = 2;
        var firstWrite = sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        await storage.BlockedAppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        dictionary.Add("interleaved", 3);
        var queuedWrite = sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        var queuedDelete = sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask();
        var queuedInitialize = sut.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        storage.ReleaseAppend.SetResult();

        foreach (var task in new[] { firstWrite, queuedWrite, queuedDelete, queuedInitialize })
        {
            var exception = await Record.ExceptionAsync(
                () => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Same(expected, exception);
        }

        Assert.Equal(2, value.Value);
        Assert.Equal(3, dictionary["interleaved"]);
        Assert.Equal(0, storage.ReadConsumeCount);
        Assert.Single(storage.Appends);
        Assert.Equal(0, storage.DeleteCount);

        var lateWrite = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Same(expected, lateWrite.InnerException);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Throws<InvalidOperationException>(() => sut.Manager.RegisterStateMachine("late", new AlwaysWritingState()));

        await sut.Manager.DisposeAsync();
        var recovered = CreateTestSystem(storage: storage);
        var recoveredValue = new DurableValue<int>("value", recovered.Manager, CreateValueCodec<int>());
        var recoveredDictionary = new DurableDictionary<string, int>("dict", recovered.Manager, CreateDictionaryCodec<string, int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        Assert.Equal(1, recoveredValue.Value);
        Assert.Single(recoveredDictionary);
        Assert.Equal(1, recoveredDictionary["persisted"]);
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("append")]
    [InlineData("replace")]
    [InlineData("delete")]
    public async Task StateManager_FailureHandling_RespectsRecoveryBoundary(string operation)
    {
        var expected = new IOException("Expected journal operation failure.");
        var storage = new CapturingStorage();
        var storageProvider = Substitute.For<IJournalStorageProvider>();
        var context = Substitute.For<IGrainContext>();
        var grainId = GrainId.Create("test-grain", "failing-journal");
        context.GrainId.Returns(grainId);
        context.ActivationServices.Returns(ServiceProvider);
        storageProvider.CreateStorage(JournalId.FromGrainId(grainId)).Returns(storage);
        var shared = new JournaledStateManagerShared(
            ServiceProvider.GetRequiredService<ILogger<JournaledStateManager>>(),
            Options.Create(ManagerOptions), TimeProvider.System, ServiceProvider);
        await using var manager = new JournaledStateManager(shared, storageProvider, context);
        var value = new DurableValue<int>("value", manager, CreateValueCodec<int>());
        if (operation == "initialize")
        {
            storage.NextReadException = expected;
            Assert.Same(expected, await Assert.ThrowsAsync<IOException>(() =>
                manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask()));
            context.DidNotReceive().Deactivate(Arg.Any<DeactivationReason>(), Arg.Any<CancellationToken>());
            await manager.InitializeAsync(TestContext.Current.CancellationToken);
            value.Value = 42;
            await manager.WriteStateAsync(TestContext.Current.CancellationToken);
            Assert.Single(storage.Appends);
            return;
        }
        else
        {
            await manager.InitializeAsync(TestContext.Current.CancellationToken);
            value.Value = 42;
            storage.NextAppendException = expected;
            storage.NextReplaceException = expected;
            storage.NextDeleteException = expected;
            storage.IsCompactionRequested = operation == "replace";
        }

        var failedOperation = operation switch
        {
            "delete" => manager.DeleteStateAsync(TestContext.Current.CancellationToken),
            _ => manager.WriteStateAsync(TestContext.Current.CancellationToken)
        };
        var exception = await Assert.ThrowsAsync<IOException>(() => failedOperation.AsTask());
        Assert.Same(expected, exception);
        context.Received(1).Deactivate(
            Arg.Is<DeactivationReason>(reason => reason.ReasonCode == DeactivationReasonCode.ApplicationError && ReferenceEquals(reason.Exception, expected)),
            Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task StateManager_StorageFailureOfPreCommitType_FencesManager()
    {
        // 存储在捕获之后报告故障，并使用与准备阶段故障相同的类型。只有准备阶段的
        // 故障会使管理器仍然可用，因此此故障会隔离管理器。
        var storage = new CapturingStorage();
        await using var manager = CreateTestSystem(storage).Manager;
        var value = new DurableValue<int>("value", manager, CreateValueCodec<int>());
        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        value.Value = 42;
        var expected = new JournaledStatePreCommitException(new IOException("Expected journal operation failure."));
        storage.NextAppendException = expected;

        Assert.Same(expected, await Assert.ThrowsAsync<JournaledStatePreCommitException>(
            () => manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StateManager_FactoryRecreationRecoversActualCommitOutcome(bool committed)
    {
        var storage = new CapturingStorage();
        var storageProvider = Substitute.For<IJournalStorageProvider>();
        var journalId = new JournalId("standalone/fail-closed");
        storageProvider.CreateStorage(journalId).Returns(storage);
        var shared = new JournaledStateManagerShared(
            ServiceProvider.GetRequiredService<ILogger<JournaledStateManager>>(),
            Options.Create(ManagerOptions), TimeProvider.System, ServiceProvider);
        var factory = new JournaledStateManagerFactory(shared, storageProvider);
        var manager = factory.CreateStandalone(journalId);
        var value = new DurableValue<int>("value", manager, CreateValueCodec<int>());
        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        value.Value = 1;
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        value.Value = 2;
        var expected = new IOException("Commit acknowledgement failed.");
        if (committed)
        {
            storage.NextPostAppendException = expected;
        }
        else
        {
            storage.NextAppendException = expected;
        }

        var exception = await Assert.ThrowsAsync<IOException>(
            () => manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Same(expected, exception);
        Assert.Equal(2, value.Value);
        await manager.DisposeAsync();

        await using var recovered = factory.CreateStandalone(journalId);
        var recoveredValue = new DurableValue<int>("value", recovered, CreateValueCodec<int>());
        await recovered.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(committed ? 2 : 1, recoveredValue.Value);
        recoveredValue.Value = 3;
        await recovered.WriteStateAsync(TestContext.Current.CancellationToken);
        storageProvider.Received(2).CreateStorage(journalId);
    }

    [Fact]
    public async Task StateManager_CancelledWriteWaitPreservesCommit()
    {
        var storage = new CapturingStorage { BlockNextAppend = true };
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;
        using var cancellation = new CancellationTokenSource();
        var write = sut.Manager.WriteStateAsync(cancellation.Token).AsTask();
        await storage.AppendEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        storage.ReleaseAppend.SetResult();
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        var recovered = CreateTestSystem(storage: storage);
        var recoveredValue = new DurableValue<int>("value", recovered.Manager, CreateValueCodec<int>());
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        Assert.Equal(42, recoveredValue.Value);
    }

    [Fact]
    public async Task StateManager_WriteStateAsync_CoalescesQueuedWrites()
    {
        var storage = new BlockingAppendStorage();
        var sut = CreateTestSystem(storage: storage);
        var state = new AlwaysWritingState();
        sut.Manager.RegisterStateMachine("state", state);
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        var first = sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        await storage.FirstAppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var second = sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        var third = sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();

        storage.AllowFirstAppend.SetResult();
        await Task.WhenAll(first, second, third)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(2, state.AppendEntriesCount);
        Assert.Equal(2, storage.Appends.Count);
    }

    [Fact]
    public async Task StateManager_DeleteStateAsync_CoalescesQueuedDeletes()
    {
        var storage = new BlockingDeleteStorage();
        var sut = CreateTestSystem(storage: storage);
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        var first = sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask();
        await storage.FirstDeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var second = sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask();
        var third = sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask();

        storage.AllowFirstDelete.SetResult();
        await Task.WhenAll(first, second, third)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(2, storage.DeleteCount);
    }

    [Fact]
    public async Task StateManager_WriteStateAsync_WaitsForActiveEntryAndCapturesIt()
    {
        var storage = new CapturingStorage();
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var state = new ManualDirectWriteState();
        sut.Manager.RegisterStateMachine("manual", state);

        // 管理器的工作循环在受控调度器上运行。发出写入信号会将循环加入该调度器的队列，
        // 循环会一直在该调度器上运行，直到遇到打开的条目，因此持续执行其排队的工作，直至
        // 队列中不再有工作，可确保写入要么已完成捕获，要么正在等待该条目。
        var scheduler = new ControlledTaskScheduler();
        var start = scheduler.Start(() => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken));
        await scheduler.RunUntilCompletedAsync(start);

        var entry = state.BeginEntry();
        entry.Writer.Write(new byte[] { 1, 2, 3 });
        var writeTask = scheduler.Start(() => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        scheduler.RunUntilIdle();

        // 写入会捕获每个状态，因此会等待占用日志缓冲区的条目。
        Assert.False(writeTask.IsCompleted);
        Assert.Empty(storage.Appends);

        state.MarkEntryClosing();
        entry.Commit();
        entry.Dispose();
        await scheduler.RunUntilCompletedAsync(writeTask);
        Assert.False(state.AppendEntriesObservedOpenEntry);
        var append = Assert.Single(storage.Appends);
        Assert.Contains(ReadBinaryEntries(append), entry => entry.StreamId.Value >= 8);
    }

    [Fact]
    public async Task StateManager_WriteWaitingForActiveEntry_PreparesChangesStagedMeanwhile()
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var entryState = new ManualDirectWriteState();
        sut.Manager.RegisterStateMachine("manual", entryState);
        var prepared = new PreparingValue("prepared", sut.Manager, CreateValueCodec<int>());

        var scheduler = new ControlledTaskScheduler();
        await scheduler.RunUntilCompletedAsync(scheduler.Start(() => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)));

        var entry = entryState.BeginEntry();
        entry.Writer.Write(new byte[] { 1, 2, 3 });
        var writeTask = scheduler.Start(() => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        scheduler.RunUntilIdle();
        Assert.False(writeTask.IsCompleted);

        // 写入等待该条目时，某个执行轮次修改了在写入捕获时才将变更记入日志的状态，
        // 如 DurableValue 所做的那样。捕获阶段会断言被捕获的状态已经过准备。
        var mutation = scheduler.Start(() =>
        {
            ((IDurableValueCommandHandler<int>)prepared).ApplySet(7);
            return Task.CompletedTask;
        });
        scheduler.RunUntilIdle();
        Assert.True(mutation.IsCompletedSuccessfully);
        Assert.False(writeTask.IsCompleted);

        entryState.MarkEntryClosing();
        entry.Commit();
        entry.Dispose();
        await scheduler.RunUntilCompletedAsync(writeTask);
        Assert.Equal(7, prepared.CapturedValue);
        Assert.Equal(1, prepared.WriteCompletedCount);
    }

    [Fact]
    public async Task StateManager_SnapshotWrite_WaitsForActiveEntryAndCapturesIt()
    {
        var storage = new CapturingStorage { IsCompactionRequested = true };
        var format = new TrackingJournalFormat(SessionPool);
        var sut = CreateTestSystem(storage: storage, journalFormat: format);
        var state = new ManualDirectWriteState();
        sut.Manager.RegisterStateMachine("manual", state);

        var scheduler = new ControlledTaskScheduler();
        var start = scheduler.Start(() => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken));
        await scheduler.RunUntilCompletedAsync(start);

        var entry = state.BeginEntry();
        entry.Writer.Write(new byte[] { 1, 2, 3 });
        var writeTask = scheduler.Start(() => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        scheduler.RunUntilIdle();

        // 快照会捕获每个状态，因此会等待另一个线程正在写入的条目。
        Assert.False(writeTask.IsCompleted);
        Assert.Empty(storage.Replaces);

        state.SnapshotPayload = [1, 2, 3];
        state.MarkEntryClosing();
        entry.Commit();
        entry.Dispose();
        await scheduler.RunUntilCompletedAsync(writeTask);

        // 快照捕获了该条目完成时所产生的状态变更，因此它取代该条目，而不是位于该条目之后。
        var replacement = Assert.Single(storage.Replaces);
        Assert.Contains(ReadBinaryEntries(replacement), entry => entry.StreamId.Value >= 8);
        Assert.Empty(storage.Appends);
        storage.IsCompactionRequested = false;
        var nextWrite = scheduler.Start(() => sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await scheduler.RunUntilCompletedAsync(nextWrite);
        Assert.Empty(storage.Appends);
    }

    /// <summary>
    /// 测试所有已注册状态能否正确地一同恢复。
    /// 验证管理器是否在从持久化状态恢复期间
    /// 维持多个集合之间的一致性。
    /// </summary>
    [Fact]
    public async Task StateManager_StateRecovery_Test()
    {
        // 准备
        var sut = CreateTestSystem();

        // 创建并填充状态
        var dictionary = new DurableDictionary<string, int>("dict1", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));
        var list = new DurableList<string>("list1", sut.Manager, new OrleansBinaryDurableListCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        dictionary.Add("key1", 1);
        dictionary.Add("key2", 2);
        list.Add("item1");
        list.Add("item2");

        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 执行 - 使用同一存储创建新的管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var recoveredDict = new DurableDictionary<string, int>("dict1", sut2.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));
        var recoveredList = new DurableList<string>("list1", sut2.Manager, new OrleansBinaryDurableListCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 状态应已恢复
        Assert.Equal(2, recoveredDict.Count);
        Assert.Equal(1, recoveredDict["key1"]);
        Assert.Equal(2, recoveredDict["key2"]);

        Assert.Equal(2, recoveredList.Count);
        Assert.Equal("item1", recoveredList[0]);
        Assert.Equal("item2", recoveredList[1]);
    }

    [Fact]
    public async Task StateManager_Recovery_UsesSelectedJournalFormatRead()
    {
        var storage = new CapturingStorage();
        var initial = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", initial.Manager, CreateValueCodec<int>());

        await initial.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;
        await initial.Manager.WriteStateAsync(CancellationToken.None);

        var format = new TrackingJournalFormat(SessionPool);
        var recovered = CreateTestSystem(storage: storage, journalFormat: format);
        var recoveredValue = new DurableValue<int>("value", recovered.Manager, CreateValueCodec<int>());

        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(42, recoveredValue.Value);
        Assert.True(format.ReadCount > 0);
    }

    [Fact]
    public async Task StateManager_RegisterState_AfterRecovery_AllocatesAboveRecoveredStreamIds()
    {
        var storage = new CapturingStorage();
        var recoveredStreamId = new JournalStreamId(12);
        using (var segment = new OrleansBinaryJournalBufferWriter())
        {
            AppendDirectorySet(segment, "existing", recoveredStreamId);
            CreateValueCodec<int>().WriteSet(42, segment.CreateJournalStreamWriter(recoveredStreamId));
            using var committed = segment.GetBuffer();
            await storage.AppendAsync(committed.AsReadOnlySequence(), CancellationToken.None);
        }

        var sut = CreateTestSystem(storage: storage);
        var existing = new DurableValue<int>("existing", sut.Manager, CreateValueCodec<int>());
        var next = new DurableValue<int>("next", sut.Manager, CreateValueCodec<int>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        next.Value = 99;
        await sut.Manager.WriteStateAsync(CancellationToken.None);

        Assert.Equal(42, existing.Value);
        var entries = ReadBinaryEntries(storage.Appends[^1]);
        Assert.Contains(entries, entry => entry.StreamId.Value == recoveredStreamId.Value + 1);
    }

    [Fact]
    public async Task StateManager_Recovery_RebindsStateAboveUnknownStreamIds()
    {
        var storage = new CapturingStorage { IsCompactionRequested = true };
        var unknownStreamId = new JournalStreamId(8);
        var unknownPayload = new byte[] { 1, 2, 3 };
        using (var segment = new OrleansBinaryJournalBufferWriter())
        {
            using (var entry = segment.CreateJournalStreamWriter(unknownStreamId).BeginEntry())
            {
                entry.Writer.Write(unknownPayload);
                entry.Commit();
            }

            using var committed = segment.GetBuffer();
            await storage.AppendAsync(committed.AsReadOnlySequence(), CancellationToken.None);
        }

        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 42;

        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        var entries = ReadBinaryEntries(Assert.Single(storage.Replaces));
        Assert.Contains(entries, entry => entry.StreamId == unknownStreamId && entry.Payload.SequenceEqual(unknownPayload));
        Assert.Contains(entries, entry => entry.StreamId.Value == unknownStreamId.Value + 1);
    }

    [Fact]
    public async Task StateManager_Recovery_ReadsConcatenatedJournalData()
    {
        var storage = new CapturingStorage();
        var initial = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", initial.Manager, CreateValueCodec<int>());

        await initial.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 1;
        await initial.Manager.WriteStateAsync(CancellationToken.None);
        value.Value = 2;
        await initial.Manager.WriteStateAsync(CancellationToken.None);

        storage.ConcatenateReads = true;
        var recovered = CreateTestSystem(storage: storage);
        var recoveredValue = new DurableValue<int>("value", recovered.Manager, CreateValueCodec<int>());

        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(2, recoveredValue.Value);
        Assert.Equal(1, storage.ReadConsumeCount);
    }

    [Fact]
    public async Task StateManager_Recovery_BuffersEntriesSplitAcrossStorageChunks()
    {
        var storage = new CapturingStorage();
        var initial = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", initial.Manager, CreateValueCodec<int>());

        await initial.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = 1;
        await initial.Manager.WriteStateAsync(CancellationToken.None);
        value.Value = 2;
        await initial.Manager.WriteStateAsync(CancellationToken.None);

        var persistedBytes = storage.Appends.SelectMany(static segment => segment).ToArray();
        var splitStorage = new ChunkedReadStorage(persistedBytes, chunkSize: 1);
        var recovered = CreateTestSystem(storage: splitStorage);
        var recoveredValue = new DurableValue<int>("value", recovered.Manager, CreateValueCodec<int>());

        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(2, recoveredValue.Value);
        Assert.Equal(persistedBytes.Length, splitStorage.ReadConsumeCount);
    }

    [Fact]
    public async Task StateManager_Recovery_RejectsMalformedTrailingData()
    {
        byte[] bytes;
        using (var segment = new OrleansBinaryJournalBufferWriter())
        {
            using (var entry = segment.CreateJournalStreamWriter(new JournalStreamId(99)).BeginEntry())
            {
                entry.Writer.Write(new byte[] { 1, 2, 3 });
                entry.Commit();
            }

            using var committed = segment.GetBuffer();
            bytes = [.. committed.ToArray(), 0x02];
        }

        var storage = new RawReadStorage(bytes);
        var sut = CreateTestSystem(storage: storage);

        InvalidOperationException exception;
        try
        {
            exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            await sut.Lifecycle.OnStop(CancellationToken.None);
        }

        Assert.Contains("journal format key 'orleans-binary'", exception.Message, StringComparison.Ordinal);
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Malformed binary journal entry stream", inner.Message, StringComparison.Ordinal);
        Assert.Contains("truncated varuint32 entry length prefix", inner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateManager_RecoveryRetry_ReplaysFixedStorage()
    {
        var validBytes = CreatePersistedValueBytes("value", 42);
        var storage = new MutableReadStorage([.. validBytes, 1, 2, 3], validBytes);
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<int>("value", sut.Manager, CreateValueCodec<int>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        await sut.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(42, value.Value);
        await sut.Lifecycle.OnStop(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StateManager_RecoveryRetry_ReplaysListWithoutDuplicatingEntries()
    {
        var seedStorage = new CapturingStorage();
        await using (var seed = CreateTestSystem(seedStorage).Manager)
        {
            var source = new DurableList<int>("items", seed,
                new OrleansBinaryDurableListCommandCodec<int>(CodecProvider.GetCodec<int>(), SessionPool));
            await seed.InitializeAsync(TestContext.Current.CancellationToken);
            source.Add(1);
            source.Add(2);
            await seed.WriteStateAsync(TestContext.Current.CancellationToken);
        }

        var bytes = seedStorage.RecoverableBytes;
        var storage = new MutableReadStorage([.. bytes, 1, 2, 3], bytes);
        await using var manager = CreateTestSystem(storage).Manager;
        var items = new DurableList<int>("items", manager,
            new OrleansBinaryDurableListCommandCodec<int>(CodecProvider.GetCodec<int>(), SessionPool));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InitializeAsync(CancellationToken.None).AsTask());
        Assert.Equal([1, 2], items);

        await manager.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2], items);
        Assert.Equal(2, storage.ReadCount);
        items.Add(3);
        await manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public async Task StateManager_RecoveryRetry_PreservesUnknownStreamOnce()
    {
        var validBytes = CreateUnknownStreamBytes(new JournalStreamId(99), [1, 2, 3]);
        var storage = new MutableReadStorage([.. validBytes, 1, 2, 3], validBytes) { IsCompactionRequested = true };
        var sut = CreateTestSystem(storage: storage);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        await sut.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var replacement = Assert.Single(storage.Replaces);
        var preserved = Assert.Single(ReadBinaryEntries(replacement), entry => entry.StreamId.Value == 99);
        Assert.Equal([1, 2, 3], preserved.Payload);
        await sut.Lifecycle.OnStop(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StateManager_RecoveryRetry_RemovesStaleRetiredPlaceholder()
    {
        var storage = new MutableReadStorage([.. CreateNamedUnknownStreamBytes("stale", new JournalStreamId(8), [1, 2, 3]), 1, 2, 3], []);
        var sut = CreateTestSystem(storage: storage);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        await sut.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(sut.Manager.TryGetStateMachine("stale", out _));
        await sut.Lifecycle.OnStop(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StateManager_Recovery_DoesNotWrapStorageReadException()
    {
        var storage = new ThrowingReadStorage();
        var sut = CreateTestSystem(storage: storage);

        InvalidOperationException exception;
        try
        {
            exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            await sut.Lifecycle.OnStop(CancellationToken.None);
        }

        Assert.Same(storage.Exception, exception);
        Assert.Null(exception.InnerException);
    }

    /// <summary>
    /// 测试多次调用 WriteStateAsync 且调用之间存在操作的情况。
    /// 验证每次 WriteStateAsync 调用是否创建一致的检查点，
    /// 以及最终状态能否正确恢复。
    /// </summary>
    [Fact]
    public async Task StateManager_MultipleWriteStates_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var dictionary = new DurableDictionary<string, int>("dict1", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 进行多次操作并在其间调用 WriteState
        dictionary.Add("key1", 1);
        await manager.WriteStateAsync(CancellationToken.None);

        dictionary.Add("key2", 2);
        await manager.WriteStateAsync(CancellationToken.None);

        dictionary["key1"] = 10;
        await manager.WriteStateAsync(CancellationToken.None);

        dictionary.Remove("key2");
        await manager.WriteStateAsync(CancellationToken.None);

        // 断言 - 最终状态正确
        Assert.Single(dictionary);
        Assert.Equal(10, dictionary["key1"]);
        Assert.False(dictionary.ContainsKey("key2"));

        // 创建新的管理器以验证恢复
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var recoveredDict = new DurableDictionary<string, int>("dict1", sut2.Manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 恢复后应得到最终状态
        Assert.Single(recoveredDict);
        Assert.Equal(10, recoveredDict["key1"]);
        Assert.False(recoveredDict.ContainsKey("key2"));
    }

    /// <summary>
    /// 测试同时管理多个不同类型的状态。
    /// 验证管理器能否在同一个 Grain 内正确处理多种数据结构
    /// （具有不同键值类型的字典、列表、值）。
    /// </summary>
    [Fact]
    public async Task StateManager_MultipleStates_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;

        // 创建多个不同类型的状态
        var intDict = new DurableDictionary<int, string>("intDict", manager, new OrleansBinaryDurableDictionaryCommandCodec<int, string>(CodecProvider.GetCodec<int>(), CodecProvider.GetCodec<string>(), SessionPool));
        var stringList = new DurableList<string>("stringList", manager, new OrleansBinaryDurableListCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool));
        var personValue = new DurableValue<TestPerson>("personValue", manager, new OrleansBinaryDurableValueCommandCodec<TestPerson>(CodecProvider.GetCodec<TestPerson>(), SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 填充所有状态
        intDict.Add(1, "one");
        intDict.Add(2, "two");

        stringList.Add("item1");
        stringList.Add("item2");

        personValue.Value = new TestPerson { Id = 100, Name = "Test Person", Age = 30 };

        await manager.WriteStateAsync(CancellationToken.None);

        // 断言 - 所有状态均应具有正确的值
        Assert.Equal(2, intDict.Count);
        Assert.Equal("one", intDict[1]);

        Assert.Equal(2, stringList.Count);
        Assert.Equal("item1", stringList[0]);

        Assert.NotNull(personValue.Value);
        Assert.Equal(100, personValue.Value.Id);
        Assert.Equal("Test Person", personValue.Value.Name);

        // 创建新的管理器以验证多个状态的恢复
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var recoveredIntDict = new DurableDictionary<int, string>("intDict", sut2.Manager, new OrleansBinaryDurableDictionaryCommandCodec<int, string>(CodecProvider.GetCodec<int>(), CodecProvider.GetCodec<string>(), SessionPool));
        var recoveredStringList = new DurableList<string>("stringList", sut2.Manager, new OrleansBinaryDurableListCommandCodec<string>(CodecProvider.GetCodec<string>(), SessionPool));
        var recoveredPersonValue = new DurableValue<TestPerson>("personValue", sut2.Manager, new OrleansBinaryDurableValueCommandCodec<TestPerson>(CodecProvider.GetCodec<TestPerson>(), SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 所有状态均应恢复并具有正确的值
        Assert.Equal(2, recoveredIntDict.Count);
        Assert.Equal("one", recoveredIntDict[1]);

        Assert.Equal(2, recoveredStringList.Count);
        Assert.Equal("item1", recoveredStringList[0]);

        Assert.NotNull(recoveredPersonValue.Value);
        Assert.Equal(100, recoveredPersonValue.Value.Id);
        Assert.Equal("Test Person", recoveredPersonValue.Value.Name);
    }

    /// <summary>
    /// 测试多个状态是否能够独立运行、互不干扰。
    /// 验证具有相似键的不同状态之间的命名空间隔离。
    /// </summary>
    [Fact]
    public async Task StateManager_Concurrency_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var manager = sut.Manager;
        var dict1 = new DurableDictionary<string, int>("dict1", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));
        var dict2 = new DurableDictionary<string, int>("dict2", manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 模拟在不同状态上的并发操作
        dict1.Add("key1", 1);
        dict2.Add("key1", 100);

        dict1.Add("key2", 2);
        dict2.Add("key2", 200);

        await manager.WriteStateAsync(CancellationToken.None);

        // 断言 - 两个状态均应具有各自正确的值
        Assert.Equal(2, dict1.Count);
        Assert.Equal(2, dict2.Count);

        Assert.Equal(1, dict1["key1"]);
        Assert.Equal(100, dict2["key1"]);

        Assert.Equal(2, dict1["key2"]);
        Assert.Equal(200, dict2["key2"]);
    }

    /// <summary>
    /// 大量数据下的状态恢复压力测试。
    /// 验证日志化系统能否处理并恢复包含
    /// 数千个条目的状态，而不丢失或损坏数据。
    /// </summary>
    [Fact]
    public async Task StateManager_LargeStateRecovery_Test()
    {
        // 准备
        var sut = CreateTestSystem();
        var largeDict = new DurableDictionary<int, string>("largeDict", sut.Manager, new OrleansBinaryDurableDictionaryCommandCodec<int, string>(CodecProvider.GetCodec<int>(), CodecProvider.GetCodec<string>(), SessionPool));
        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 执行 - 添加大量元素
        const int itemCount = 1000;
        for (int i = 0; i < itemCount; i++)
        {
            largeDict.Add(i, $"Value {i}");
        }

        await sut.Manager.WriteStateAsync(CancellationToken.None);

        // 创建用于恢复的新管理器
        var sut2 = CreateTestSystem(storage: sut.Storage);
        var recoveredDict = new DurableDictionary<int, string>("largeDict", sut2.Manager, new OrleansBinaryDurableDictionaryCommandCodec<int, string>(CodecProvider.GetCodec<int>(), CodecProvider.GetCodec<string>(), SessionPool));
        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 断言 - 所有元素均应恢复
        Assert.Equal(itemCount, recoveredDict.Count);
        for (int i = 0; i < itemCount; i++)
        {
            Assert.Equal($"Value {i}", recoveredDict[i]);
        }
    }

    /// <summary>
    /// 测试退役状态的完整生命周期。早期压实会保留该状态，它也可以重新引入；
    /// 但在宽限期届满后的后续压实中，最终会被清除。
    /// </summary>
    [Fact]
    public async Task StateManager_AutoRetiringStates()
    {
        const string DictToKeepKey = "dictToKeep";
        const string DictToRetireKey = "dictToRetire";

        var period = ManagerOptions.RetirementGracePeriod;
        var timeProvider = new FakeTimeProvider(DateTime.UtcNow);
        var storage = new VolatileJournalStorage(null, new()
        {
            MaxAppendsBeforeSnapshot = 10
        });

        // -------------- 步骤 1 --------------

        // 初始有 2 个字典，其中一个通过不在管理器中注册的方式将其退役。
        // 在实际场景中，这对应开发人员从 Grain 的构造函数依赖项中移除该字典。
        var sut1 = CreateTestSystem(storage, timeProvider);
        var dictToKeep1 = CreateTestState(DictToKeepKey, sut1.Manager);
        var dictToRetire2 = CreateTestState(DictToRetireKey, sut1.Manager);

        await sut1.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        dictToKeep1.Add("a", 1);
        dictToRetire2.Add("b", 1);

        await sut1.Manager.WriteStateAsync(CancellationToken.None);

        // -------------- 步骤 2 --------------

        // 这次仅注册要保留的字典，这会将 dictToRetire 标记为已退役。
        var sut2 = CreateTestSystem(storage, timeProvider);
        var dictToKeep2 = CreateTestState(DictToKeepKey, sut2.Manager);

        await sut2.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 管理器应已恢复 dictToKeep 的状态，
        // 并为 dictToRetire 创建 DurableNothing 占位符（此时无法对此进行测试）。
        Assert.Equal(1, dictToKeep2["a"]);

        // 将时间推进半个宽限期，以验证能否避免该状态被清除。
        timeProvider.Advance(period / 2);

        await TriggerCompaction(sut2.Manager, dictToKeep2);

        // -------------- 步骤 3 --------------

        // 验证本次压实未清除已退役的字典，因为时间仅过去了一半。
        var sut3 = CreateTestSystem(storage, timeProvider);
        var dictToKeep3 = CreateTestState(DictToKeepKey, sut3.Manager);
        var dictToRetire3 = CreateTestState(DictToRetireKey, sut3.Manager);

        await sut3.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal(10, dictToKeep3["a"]);

        // 条目 ["b", 1] 的存在证明 dictToRetire 的状态得到了保留，尽管我们在步骤 2 中未注册它。
        Assert.Equal(1, dictToRetire3["b"]);

        // 再推进半个宽限期，使时间累计达到完整宽限期。但由于已重新引入 dictToRetire，它应已解除退役状态。
        // 这与步骤 2 类似，但步骤 2 是由于时间尚未届满而避免清除，此处则因重新注册而避免清除。
        timeProvider.Advance(period / 2);

        dictToRetire3["b"] = 2;
        await TriggerCompaction(sut3.Manager, dictToKeep3);

        var sut3Recovered = CreateTestSystem(storage, timeProvider);
        var dictToRetire3Recovered = CreateTestState(DictToRetireKey, sut3Recovered.Manager);
        await sut3Recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        Assert.Equal(2, dictToRetire3Recovered["b"]);

        // -------------- 步骤 4 --------------

        // 由于步骤 3 中重新注册了 dictToRetire（以验证它未被清除），它已从跟踪器中移除。
        // 与步骤 2 相同，再次仅注册要保留的字典，这会将 dictToRetire 标记为已退役。
        var sut4 = CreateTestSystem(storage, timeProvider);
        var dictToKeep4 = CreateTestState(DictToKeepKey, sut4.Manager);

        await sut4.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        // 管理器应已恢复 dictToKeep 的状态。
        // 它应已为 dictToRetire 创建 DurableNothing 占位符，但我们无法对此进行测试。


        // 这次将时间推进一个完整宽限期。注意这样做是必要的，因为步骤 3 的一个副作用
        // 是 dictToRetire 从跟踪器中被移除（因为它重新引入了），因此仅触发压实并不足够，
        // 因为退役时间实质上会被重置为“现在”。
        timeProvider.Advance(period);

        // 此次压实最终应将其清除。
        await TriggerCompaction(sut4.Manager, dictToKeep4);

        // -------------- 步骤 5 --------------

        // 此时，管理器已生成快照，因此应已清除 dictToRetire 的数据。
        // 再次注册两个字典，便可查看快照生成后保留了哪些状态。
        var sut5 = CreateTestSystem(storage, timeProvider);
        var dictToKeep5 = CreateTestState(DictToKeepKey, sut5.Manager);
        var dictToRetire5 = CreateTestState(DictToRetireKey, sut5.Manager);

        await sut5.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        Assert.Equal(10, dictToKeep5["a"]);

        // 已退役的字典现在应为空，因为其状态已在压实期间被清除。
        // 注意，这是新版 dictToRetire，因为原版已被移除。这里的思路是，
        // 如果能注册新的 dictToRetire（使用相同的键），就意味着状态本身
        // 及其数据均已被移除，否则先前的状态至少会包含一个
        // 条目，即 ["b", 1]。

        Assert.Empty(dictToRetire5);

        // 注意：状态退役的一个好处是能够复用状态名称。

        DurableDictionary<string, int> CreateTestState(string key, IJournaledStateManager manager) =>
            new(key, manager, new OrleansBinaryDurableDictionaryCommandCodec<string, int>(CodecProvider.GetCodec<string>(), CodecProvider.GetCodec<int>(), SessionPool));

        static async Task TriggerCompaction(IJournaledStateManager manager, DurableDictionary<string, int> dict)
        {
            for (var i = 0; i < 11; i++)
            {
                dict["a"] = i;
                await manager.WriteStateAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task WorkLoop_BlockedAppendFencesQueuedDeleteUntilAppendCompletes()
    {
        var storage = new CapturingStorage { BlockNextAppend = true };
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<string>("value", sut.Manager, CreateValueCodec<string>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = "before-delete";
        var append = sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask();
        await storage.AppendEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var deleteEntered = storage.DeleteEntered.Task;
        var delete = sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.Equal(["append"], storage.OperationLog);
        Assert.False(deleteEntered.IsCompleted);
        Assert.Equal(0, storage.DeleteCount);

        storage.ReleaseAppend.SetResult();
        await Task.WhenAll(append, delete).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(["append", "delete"], storage.OperationLog);
        Assert.False(storage.DeleteEnteredWhileAppendInProgress);
        Assert.Single(storage.Appends);
        Assert.Equal(1, storage.DeleteCount);
    }

    [Fact]
    public async Task WorkLoop_AppendDeleteReplacePersistsPostDeleteStateAndFreshManagerRecoversIt()
    {
        var storage = new CapturingStorage();
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<string>("value", sut.Manager, CreateValueCodec<string>());

        await sut.Lifecycle.OnStart(TestContext.Current.CancellationToken);
        value.Value = "before-delete";
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        await sut.Manager.DeleteStateAsync(TestContext.Current.CancellationToken);
        value.Value = "after-delete";
        storage.IsCompactionRequested = true;
        await sut.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["append", "delete", "replace"], storage.OperationLog);
        var appendBytes = Assert.Single(storage.Appends);
        var replacementBytes = Assert.Single(storage.Replaces);

        var appendReplay = ReplayValueCommands(appendBytes);
        Assert.Equal("before-delete", appendReplay.Value);
        Assert.Equal(["before-delete"], appendReplay.AppliedValues);

        var replacementReplay = ReplayValueCommands(replacementBytes);
        Assert.Equal("after-delete", replacementReplay.Value);
        Assert.Equal(["after-delete"], replacementReplay.AppliedValues);
        Assert.DoesNotContain("before-delete", replacementReplay.AppliedValues);
        Assert.Equal(replacementBytes, storage.RecoverableBytes);

        var recoveryCodec = new TrackingValueCodec<string>(CreateValueCodec<string>());
        var recovered = CreateTestSystem(storage: storage);
        var recoveredValue = new DurableValue<string>("value", recovered.Manager, recoveryCodec);
        await recovered.Lifecycle.OnStart(TestContext.Current.CancellationToken);

        Assert.Equal("after-delete", recoveredValue.Value);
        Assert.Equal(["after-delete"], recoveryCodec.AppliedValues);
        Assert.DoesNotContain("before-delete", recoveryCodec.AppliedValues);
    }

    [Fact]
    public async Task RecoverAsync_UnsupportedLegacyRecordRecoversInNewManagerAfterRepair()
    {
        var unsupportedBytes = CreateUnsupportedLegacyCommandVersionRecord(streamId: 128, commandVersion: 1);
        var validBytes = CreatePersistedStringValueBytes("value", "recovered");
        var storage = new MutableReadStorage(blockedReadNumber: 2, unsupportedBytes, validBytes);
        var codec = new TrackingValueCodec<string>(CreateValueCodec<string>());
        var sut = CreateTestSystem(storage: storage);
        var value = new DurableValue<string>("value", sut.Manager, codec);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.Lifecycle.OnStart(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Equal(
            "Failed to recover journaling state using journal format key 'orleans-binary'. " +
            "The configured write journal format key is 'orleans-binary'.",
            exception.Message);
        var inner = Assert.IsType<NotSupportedException>(exception.InnerException);
        Assert.Equal("Unsupported legacy binary journal command format version at byte offset 0: 1.", inner.Message);
        Assert.Null(value.Value);
        Assert.Empty(codec.AppliedValues);
        Assert.Equal(unsupportedBytes, storage.Bytes);
        Assert.Empty(storage.Replaces);
        Assert.Empty(storage.OperationLog);

        await sut.Manager.DisposeAsync();
        sut = CreateTestSystem(storage: storage);
        value = new DurableValue<string>("value", sut.Manager, codec);
        var initialization = sut.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        await storage.BlockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        storage.AllowBlockedRead.SetResult();
        await initialization.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("recovered", value.Value);
        Assert.Equal(["recovered"], codec.AppliedValues);
        Assert.Equal(validBytes, storage.Bytes);
        Assert.Empty(storage.Replaces);
        Assert.Empty(storage.OperationLog);
        await sut.Lifecycle.OnStop(TestContext.Current.CancellationToken);
    }

    private sealed class StreamingOnlyStorage : IJournalStorage
    {
        public bool StreamingReadCalled { get; private set; }

        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            StreamingReadCalled = true;
            consumer.Complete(metadata: null);
            return default;
        }

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }

    private OrleansBinaryDurableDictionaryCommandCodec<K, V> CreateDictionaryCodec<K, V>() where K : notnull =>
        new(CodecProvider.GetCodec<K>(), CodecProvider.GetCodec<V>(), SessionPool);

    private OrleansBinaryDurableValueCommandCodec<T> CreateValueCodec<T>() =>
        new(CodecProvider.GetCodec<T>(), SessionPool);

    private byte[] CreatePersistedValueBytes(string name, int value)
    {
        using var segment = new OrleansBinaryJournalBufferWriter();
        AppendDirectorySet(segment, name, new JournalStreamId(8));
        var codec = CreateValueCodec<int>();
        codec.WriteSet(value, segment.CreateJournalStreamWriter(new JournalStreamId(8)));

        using var committed = segment.GetBuffer();
        return committed.ToArray();
    }

    private byte[] CreatePersistedStringValueBytes(string name, string value)
    {
        using var segment = new OrleansBinaryJournalBufferWriter();
        AppendDirectorySet(segment, name, new JournalStreamId(8));
        CreateValueCodec<string>().WriteSet(value, segment.CreateJournalStreamWriter(new JournalStreamId(8)));

        using var committed = segment.GetBuffer();
        return committed.ToArray();
    }

    private static byte[] CreateUnsupportedLegacyCommandVersionRecord(ulong streamId, byte commandVersion)
    {
        using var writer = new ArcBufferWriter();
        var serializerWriter = Writer.Create(writer, session: null!);
        serializerWriter.WriteVarUInt32(checked((uint)(GetVarUInt64ByteCount(streamId) + 1)));
        serializerWriter.WriteVarUInt64(streamId);
        serializerWriter.WriteByte(commandVersion);
        serializerWriter.Commit();
        using var buffer = writer.PeekSlice(writer.Length);
        return buffer.ToArray();
    }

    private static int GetVarUInt64ByteCount(ulong value)
    {
        var result = 1;
        while (value >= 128)
        {
            value >>= 7;
            result++;
        }

        return result;
    }

    private byte[] CreateUnknownStreamBytes(JournalStreamId streamId, ReadOnlySpan<byte> payload)
    {
        using var segment = new OrleansBinaryJournalBufferWriter();
        using (var entry = segment.CreateJournalStreamWriter(streamId).BeginEntry())
        {
            entry.Writer.Write(payload);
            entry.Commit();
        }

        using var committed = segment.GetBuffer();
        return committed.ToArray();
    }

    private byte[] CreateNamedUnknownStreamBytes(string name, JournalStreamId streamId, ReadOnlySpan<byte> payload)
    {
        using var segment = new OrleansBinaryJournalBufferWriter();
        AppendDirectorySet(segment, name, streamId);
        using (var entry = segment.CreateJournalStreamWriter(streamId).BeginEntry())
        {
            entry.Writer.Write(payload);
            entry.Commit();
        }

        using var committed = segment.GetBuffer();
        return committed.ToArray();
    }

    private void AppendDirectorySet(OrleansBinaryJournalBufferWriter segment, string name, JournalStreamId streamId)
    {
        var codec = CreateDictionaryCodec<string, ulong>();
        codec.WriteSet(name, streamId.Value, segment.CreateJournalStreamWriter(new JournalStreamId(0)));
    }

    private static ArcBuffer CreateBuffer(ReadOnlySpan<byte> value)
    {
        using var writer = new ArcBufferWriter();
        writer.Write(value);
        return writer.ConsumeSlice(writer.Length);
    }

    private static int GetCommittedLength(JournalBufferWriter writer)
    {
        using var buffer = writer.GetBuffer();
        return buffer.Length;
    }

    private List<CapturedJournalEntry> ReadBinaryEntries(ReadOnlySpan<byte> bytes)
    {
        using var writer = new ArcBufferWriter();
        writer.Write(bytes);
        var reader = new JournalBufferReader(writer.Reader, isCompleted: true);
        var consumer = new CapturingJournalEntrySink();
        var context = JournalTestReplayContext.Create(OrleansBinaryJournalFormat.JournalFormatKey, consumer.Bind(ReadStreamIds(bytes)));
        ((IJournalFormat)new OrleansBinaryJournalFormat(SessionPool)).Replay(reader, context);
        Assert.Equal(0, reader.Length);

        return consumer.Entries;
    }

    private (string? Value, IReadOnlyList<string> AppliedValues) ReplayValueCommands(ReadOnlySpan<byte> bytes)
    {
        var entries = ReadBinaryEntries(bytes);
        var streamId = Assert.Single(entries.Where(static entry => entry.StreamId.Value >= 8).Select(static entry => entry.StreamId).Distinct());
        var codec = new TrackingValueCodec<string>(CreateValueCodec<string>());
        var state = new RecordingValueState<string>(codec);
        var context = JournalTestReplayContext.Create(OrleansBinaryJournalFormat.JournalFormatKey, (streamId, state));

        foreach (var entry in entries.Where(entry => entry.StreamId == streamId))
        {
            ((IStateMachine)state).ReplayEntry(
                new JournalEntry(OrleansBinaryJournalFormat.JournalFormatKey, CodecTestHelpers.ReadBuffer(entry.Payload)),
                context);
        }

        return (state.Value, codec.AppliedValues);
    }

    private static List<JournalStreamId> ReadStreamIds(ReadOnlySpan<byte> bytes)
    {
        var streamIds = new List<JournalStreamId>();
        using var writer = new ArcBufferWriter();
        writer.Write(bytes);
        using var buffer = writer.PeekSlice(writer.Length);
        var offset = 0;

        while (offset < buffer.Length)
        {
            var remaining = buffer.UnsafeSlice(offset, buffer.Length - offset);
            if (!OrleansBinaryJournalReader.TryReadVersionAndLength(remaining, out var version, out var length, out var lengthPrefixLength))
            {
                throw new InvalidOperationException("The binary journal entry stream is malformed.");
            }

            var entryStart = offset + lengthPrefixLength;
            if (length == 0 || length > buffer.Length - entryStart)
            {
                throw new InvalidOperationException("The binary journal entry stream is malformed.");
            }

            var entry = buffer.UnsafeSlice(entryStart, checked((int)length));
            var streamIdValue = version == OrleansBinaryJournalReader.FramingVersion
                ? OrleansBinaryJournalReader.ReadUInt32LittleEndian(entry.UnsafeSlice(0, sizeof(uint)))
                : checked((uint)Reader.Create(entry, session: null!).ReadVarUInt64());

            var streamId = new JournalStreamId(streamIdValue);
            if (!streamIds.Contains(streamId))
            {
                streamIds.Add(streamId);
            }

            offset = checked(entryStart + (int)length);
        }

        return streamIds;
    }

    private readonly record struct CapturedJournalEntry(JournalStreamId StreamId, byte[] Payload);

    private static void AssertContainsRuntimeAndApplicationEntries(IReadOnlyCollection<CapturedJournalEntry> entries)
    {
        Assert.Contains(entries, entry => entry.StreamId.Value == 0 && entry.Payload.Length > 0);
        Assert.Contains(entries, entry => entry.StreamId.Value >= 8 && entry.Payload.Length > 0);
    }

    private sealed class CapturingJournalEntrySink
    {
        public List<CapturedJournalEntry> Entries { get; } = [];

        public (JournalStreamId StreamId, IStateMachine State)[] Bind(IEnumerable<JournalStreamId> streamIds)
        {
            return streamIds.Select(streamId => (streamId, (IStateMachine)new StreamSink(this, streamId))).ToArray();
        }

        private sealed class StreamSink(CapturingJournalEntrySink owner, JournalStreamId streamId) : IStateMachine
        {
            void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) =>
                owner.Entries.Add(new(streamId, entry.Reader.ToArray()));

            public void Reset(JournalStreamWriter writer) { }
            public void WritePendingEntries(JournalStreamWriter writer) { }
            public void WriteSnapshot(JournalStreamWriter writer) { }
        }
    }

    private sealed class DecodedPayloadOnlyJournalFormat : IJournalFormat
    {
        private readonly JournalStreamId _streamId;
        private readonly byte[] _payload;
        private readonly TrackingJournalFormat _writerFormat;

        public DecodedPayloadOnlyJournalFormat(JournalStreamId streamId, byte[] payload, SerializerSessionPool sessionPool)
        {
            _streamId = streamId;
            _payload = payload.ToArray();
            _writerFormat = new TrackingJournalFormat(sessionPool);
        }

        public List<TrackingJournalBufferWriter> Writers => _writerFormat.Writers;

        public string FormatKey => OrleansBinaryJournalFormat.JournalFormatKey;

        public string? MimeType => null;

        public JournalBufferWriter CreateWriter() => _writerFormat.CreateWriter();

        public void Replay(JournalBufferReader input, JournalReplayContext context)
        {
            if (input.Length == 0)
            {
                return;
            }

            var callbackPayload = _payload.ToArray();
            var state = context.ResolveStateMachine(_streamId);
            state.ReplayEntry(new JournalEntry(FormatKey, CodecTestHelpers.ReadBuffer(callbackPayload)), context);

            Array.Fill(callbackPayload, byte.MaxValue);
            input.Skip(input.Length);
        }
    }

    private sealed class TestPreservedJournalEntry(ReadOnlyMemory<byte> payload) : IPreservedJournalEntry
    {
        public ReadOnlyMemory<byte> Payload { get; } = payload.ToArray();

        public string FormatKey => OrleansBinaryJournalFormat.JournalFormatKey;

    }

    private sealed class NonConsumingJournalFormat : IJournalFormat
    {
        public string FormatKey => OrleansBinaryJournalFormat.JournalFormatKey;

        public string? MimeType => null;

        public JournalBufferWriter CreateWriter() => new OrleansBinaryJournalBufferWriter();

        public void Replay(JournalBufferReader input, JournalReplayContext context)
        {
        }
    }

    private sealed class TrackingJournalFormat(SerializerSessionPool sessionPool) : IJournalFormat
    {
        private readonly OrleansBinaryJournalFormat _inner = new(sessionPool);

        public List<TrackingJournalBufferWriter> Writers { get; } = [];

        public int ReadCount { get; private set; }

        public string FormatKey => OrleansBinaryJournalFormat.JournalFormatKey;

        public string? MimeType => null;

        public JournalBufferWriter CreateWriter()
        {
            var writer = new TrackingJournalBufferWriter();
            Writers.Add(writer);
            return writer;
        }

        public void Replay(JournalBufferReader input, JournalReplayContext context)
        {
            ReadCount++;
            ((IJournalFormat)_inner).Replay(input, context);
        }
    }

    private sealed class TrackingJournalBufferWriter : OrleansBinaryJournalBufferWriter
    {
        public List<uint> BeganEntryIds { get; } = [];

        protected override void StartEntry(JournalStreamId streamId)
        {
            BeganEntryIds.Add(streamId.Value);
            base.StartEntry(streamId);
        }

        protected override void WritePreservedEntry(JournalStreamId streamId, IPreservedJournalEntry entry)
        {
            BeganEntryIds.Add(streamId.Value);
            base.WritePreservedEntry(streamId, entry);
        }
    }

    private readonly record struct MetricMeasurement<T>(T Value, string? Operation, string? Status);

    private static MeterListener CreateMetricListener<T>(string instrumentName, ConcurrentBag<MetricMeasurement<T>> measurements)
        where T : struct
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Microsoft.Orleans" && instrument.Name == instrumentName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<T>((_, measurement, tags, _) =>
            measurements.Add(new(measurement, GetTag(tags, "operation"), GetTag(tags, "status"))));
        listener.Start();
        return listener;
    }

    private static string? GetTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == name)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private sealed class BlockingAppendStorage : IJournalStorage
    {
        private int _appendCount;

        public TaskCompletionSource FirstAppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFirstAppend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<byte[]> Appends { get; } = [];

        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            consumer.Complete(metadata: null);
            return default;
        }

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _appendCount) == 1)
            {
                FirstAppendStarted.SetResult();
                await AllowFirstAppend.Task.WaitAsync(cancellationToken);
            }

            Appends.Add(value.ToArray());
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }

    private sealed class BlockingDeleteStorage : IJournalStorage
    {
        public TaskCompletionSource FirstDeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFirstDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DeleteCount { get; private set; }

        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            consumer.Complete(metadata: null);
            return default;
        }

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public async ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCount++;
            if (DeleteCount == 1)
            {
                FirstDeleteStarted.SetResult();
                await AllowFirstDelete.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class CapturingStorage : IJournalStorage
    {
        private readonly object _lock = new();
        private readonly List<byte[]> _segments = [];
        private int _activeAppends;

        public List<byte[]> Appends { get; } = [];

        public List<byte[]> Replaces { get; } = [];

        public List<byte[]> FailedReplaceAttempts { get; } = [];

        public List<string> OperationLog { get; } = [];

        public byte[] RecoverableBytes
        {
            get
            {
                lock (_lock)
                {
                    return _segments.SelectMany(static segment => segment).ToArray();
                }
            }
        }

        public bool BlockNextAppend { get; set; }

        public TaskCompletionSource AppendEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BlockedAppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseAppend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DeleteEnteredWhileAppendInProgress { get; private set; }

        public bool BlockNextReplace { get; set; }

        public TaskCompletionSource ReplaceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseReplace { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ConcatenateReads { get; set; }

        public int DeleteCount { get; private set; }

        public Exception? NextAppendException { get; set; }

        public Exception? NextPostAppendException { get; set; }

        public Exception? NextDeleteException { get; set; }

        public Exception? NextReadException { get; set; }

        public Exception? NextReplaceException { get; set; }

        public int ReplaceAttemptCount { get; private set; }

        public int ReadConsumeCount { get; private set; }

        public void ResetReadConsumeCount()
        {
            lock (_lock)
            {
                ReadConsumeCount = 0;
            }
        }

        public bool IsCompactionRequested { get; set; }

        public bool DelayReplace { get; set; }

        public TaskCompletionSource ReplaceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowReplace { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(consumer);
            if (NextReadException is { } exception)
            {
                NextReadException = null;
                throw exception;
            }

            byte[][] segments;
            lock (_lock)
            {
                segments = _segments.ToArray();
            }

            if (ConcatenateReads)
            {
                var totalLength = 0;
                foreach (var segment in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalLength += segment.Length;
                }

                if (totalLength > 0)
                {
                    var concatenated = new byte[totalLength];
                    var offset = 0;
                    foreach (var segment in segments)
                    {
                        segment.CopyTo(concatenated.AsSpan(offset));
                        offset += segment.Length;
                    }

                    lock (_lock)
                    {
                        ReadConsumeCount++;
                    }

                    consumer.Read(concatenated, metadata: null, complete: true);
                }
                else
                {
                    consumer.Complete(metadata: null);
                }

                return;
            }

            consumer.Read(GetSegments(), metadata: null, complete: true);

            IEnumerable<ReadOnlyMemory<byte>> GetSegments()
            {
                foreach (var segment in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_lock)
                    {
                        ReadConsumeCount++;
                    }

                    yield return segment;
                }
            }
        }

        public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Exception? exceptionToThrow;
            lock (_lock)
            {
                ReplaceAttemptCount++;
                exceptionToThrow = NextReplaceException;
                if (exceptionToThrow is not null)
                {
                    NextReplaceException = null;
                    FailedReplaceAttempts.Add(value.ToArray());
                    OperationLog.Add("replace-failed");
                }
                else
                {
                    OperationLog.Add("replace");
                }
            }

            if (exceptionToThrow is not null)
            {
                ExceptionDispatchInfo.Throw(exceptionToThrow);
            }

            ReplaceEntered.TrySetResult();
            if (BlockNextReplace)
            {
                BlockNextReplace = false;
                await ReleaseReplace.Task.WaitAsync(cancellationToken);
            }

            if (DelayReplace)
            {
                ReplaceStarted.SetResult();
                await AllowReplace.Task.WaitAsync(cancellationToken);
            }

            var bytes = value.ToArray();
            lock (_lock)
            {
                Replaces.Add(bytes);
                _segments.Clear();
                _segments.Add(bytes);
            }
        }

        public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _activeAppends);
            try
            {
                Exception? exceptionToThrow;
                lock (_lock)
                {
                    exceptionToThrow = NextAppendException;
                    if (exceptionToThrow is not null)
                    {
                        NextAppendException = null;
                        OperationLog.Add("append-failed");
                    }
                    else
                    {
                        OperationLog.Add("append");
                    }
                }

                AppendEntered.TrySetResult();
                if (BlockNextAppend)
                {
                    BlockNextAppend = false;
                    BlockedAppendStarted.SetResult();
                    await ReleaseAppend.Task.WaitAsync(cancellationToken);
                }

                if (exceptionToThrow is not null)
                {
                    ExceptionDispatchInfo.Throw(exceptionToThrow);
                }

                var bytes = value.ToArray();
                lock (_lock)
                {
                    Appends.Add(bytes);
                    _segments.Add(bytes);
                }

                if (NextPostAppendException is { } postAppendException)
                {
                    NextPostAppendException = null;
                    throw postAppendException;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeAppends);
            }
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextDeleteException is { } exception)
            {
                NextDeleteException = null;
                throw exception;
            }

            lock (_lock)
            {
                DeleteEnteredWhileAppendInProgress = Volatile.Read(ref _activeAppends) > 0;
                OperationLog.Add("delete");
                DeleteCount++;
                _segments.Clear();
            }

            DeleteEntered.TrySetResult();
            return default;
        }
    }

    private sealed class RawReadStorage(byte[] bytes) : IJournalStorage
    {
        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(consumer);
            cancellationToken.ThrowIfCancellationRequested();
            consumer.Read(bytes, metadata: null, complete: true);
            return default;
        }

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }

    private sealed class MutableReadStorage : IJournalStorage
    {
        private readonly object _lock = new();
        private readonly Queue<byte[]> _readSnapshots = [];
        private readonly int _blockedReadNumber;
        private byte[] _bytes;
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public MutableReadStorage(params byte[][] readSnapshots) : this(blockedReadNumber: 0, readSnapshots)
        {
        }

        public MutableReadStorage(int blockedReadNumber, params byte[][] readSnapshots)
        {
            // 恢复重试测试模拟存储在读取失败后被修复的情况。
            // 该序列使修复确定性地发生，而不是与管理器的重试产生竞态。
            if (readSnapshots.Length == 0)
            {
                throw new ArgumentException("At least one read snapshot is required.", nameof(readSnapshots));
            }

            foreach (var readSnapshot in readSnapshots)
            {
                ArgumentNullException.ThrowIfNull(readSnapshot);
                _readSnapshots.Enqueue(readSnapshot.ToArray());
            }

            _blockedReadNumber = blockedReadNumber;
            _bytes = readSnapshots[^1].ToArray();
        }

        public byte[] Bytes
        {
            get
            {
                lock (_lock)
                {
                    return _bytes.ToArray();
                }
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (_lock)
                {
                    _readSnapshots.Clear();
                    _bytes = value.ToArray();
                }
            }
        }

        public List<byte[]> Replaces { get; } = [];

        public List<string> OperationLog { get; } = [];

        public bool IsCompactionRequested { get; set; }

        public TaskCompletionSource BlockedReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowBlockedRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ReadToken { get; private set; }

        public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(consumer);
            ReadToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _readCount) == _blockedReadNumber)
            {
                BlockedReadStarted.SetResult();
                await AllowBlockedRead.Task.WaitAsync(cancellationToken);
            }

            byte[] snapshot;
            lock (_lock)
            {
                if (_readSnapshots.TryDequeue(out var readSnapshot))
                {
                    _bytes = readSnapshot;
                    snapshot = readSnapshot.ToArray();
                }
                else
                {
                    snapshot = _bytes.ToArray();
                }
            }

            consumer.Read(snapshot, metadata: null, complete: true);
        }

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = value.ToArray();
            OperationLog.Add("replace");
            Replaces.Add(bytes);
            lock (_lock)
            {
                _bytes = bytes.ToArray();
            }

            return default;
        }

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appendBytes = value.ToArray();
            OperationLog.Add("append");
            lock (_lock)
            {
                _bytes = [.. _bytes, .. appendBytes];
            }

            return default;
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationLog.Add("delete");
            Bytes = [];
            return default;
        }
    }

    private sealed class ThrowingReadStorage : IJournalStorage
    {
        public InvalidOperationException Exception { get; } = new("Storage read failed.");

        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
            => ValueTask.FromException(Exception);

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }

    private sealed class ChunkedReadStorage(byte[] bytes, int chunkSize) : IJournalStorage
    {
        public int ReadConsumeCount { get; private set; }

        public bool IsCompactionRequested => false;

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(consumer);

            consumer.Read(GetChunks(), metadata: null, complete: true);
            return default;

            IEnumerable<ReadOnlyMemory<byte>> GetChunks()
            {
                for (var offset = 0; offset < bytes.Length; offset += chunkSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var length = Math.Min(chunkSize, bytes.Length - offset);
                    ReadConsumeCount++;
                    yield return new ReadOnlyMemory<byte>(bytes, offset, length);
                }
            }
        }

        public ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken) => default;

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }

    private sealed class DelayedBorrowingStorage : IJournalStorage
    {
        public byte[]? AppendBytesAfterYield { get; private set; }

        public byte[]? ReplaceBytesAfterYield { get; private set; }

        public bool IsCompactionRequested { get; set; }

        public ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
        {
            consumer.Complete(metadata: null);
            return default;
        }

        public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceBytesAfterYield = value.ToArray();
        }

        public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            AppendBytesAfterYield = value.ToArray();
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken) => default;
    }

    /// <summary>
    /// 仅在测试要求时执行排队的任务，每次执行一个批次。
    /// </summary>
    private sealed class ControlledTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> _tasks = new();
        private readonly SemaphoreSlim _queued = new(0);

        public Task Start(Func<Task> action) =>
            Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, this).Unwrap();

        /// <summary>
        /// 执行排队的任务，直到 <paramref name="completion"/> 完成，包括其他线程加入队列的任务。
        /// </summary>
        public async Task RunUntilCompletedAsync(Task completion)
        {
            RunUntilIdle();
            while (!completion.IsCompleted)
            {
                Assert.True(await _queued.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "No work was queued.");
                RunUntilIdle();
            }

            await completion;
        }

        /// <summary>
        /// 执行排队的任务，包括这些任务加入队列的延续操作，直到队列为空。
        /// </summary>
        public void RunUntilIdle()
        {
            // 捕获上下文的延续操作在此调度器上恢复执行，而不是在测试的同步上下文中恢复执行。
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                while (_tasks.TryDequeue(out var task))
                {
                    TryExecuteTask(task);
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }
        }

        protected override void QueueTask(Task task)
        {
            _tasks.Enqueue(task);
            _queued.Release();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        protected override IEnumerable<Task> GetScheduledTasks() => _tasks.ToArray();
    }

    private sealed class ManualDirectWriteState : IStateMachine
    {
        private JournalStreamWriter _writer;
        private bool _entryOpen;

        public bool AppendEntriesObservedOpenEntry { get; private set; }

        public JournalEntryScope BeginEntry()
        {
            _entryOpen = true;
            return _writer.BeginEntry();
        }

        public void MarkEntryClosing() => _entryOpen = false;

        void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) { }

        public void Reset(JournalStreamWriter writer) => _writer = writer;

        public void WritePendingEntries(JournalStreamWriter writer)
        {
            AppendEntriesObservedOpenEntry |= _entryOpen;
        }

        public byte[]? SnapshotPayload { get; set; }

        public void WriteSnapshot(JournalStreamWriter writer)
        {
            if (SnapshotPayload is { } payload)
            {
                using var entry = writer.BeginEntry();
                entry.Writer.Write(payload);
                entry.Commit();
            }
        }
    }

    private sealed class AlwaysWritingState : IStateMachine
    {
        public int AppendEntriesCount { get; private set; }

        public int WriteCompletedCount { get; private set; }

        void IStateMachine.ReplayEntry(JournalEntry entry, JournalReplayContext context) { }

        public void Reset(JournalStreamWriter writer) { }

        public void WritePendingEntries(JournalStreamWriter writer)
        {
            AppendEntriesCount++;
            using var entry = writer.BeginEntry();
            entry.Writer.GetSpan(1)[0] = 1;
            entry.Writer.Advance(1);
            entry.Commit();
        }

        public void WriteSnapshot(JournalStreamWriter writer) => WritePendingEntries(writer);

        public void OnWriteCompleted() => WriteCompletedCount++;

    }

    private sealed class TrackingValueCodec<T>(IDurableValueCommandCodec<T> inner) : IDurableValueCommandCodec<T>
    {
        public List<T> AppliedValues { get; } = [];

        public void WriteSet(T value, JournalStreamWriter writer) => inner.WriteSet(value, writer);

        public void Apply(JournalBufferReader input, IDurableValueCommandHandler<T> consumer) =>
            inner.Apply(input, new TrackingHandler(this, consumer));

        private sealed class TrackingHandler(TrackingValueCodec<T> owner, IDurableValueCommandHandler<T> inner) : IDurableValueCommandHandler<T>
        {
            public void ApplySet(T value)
            {
                owner.AppliedValues.Add(value);
                inner.ApplySet(value);
            }
        }
    }

    private sealed class ThrowingDictionarySetCodec<K, V> : IDurableDictionaryCommandCodec<K, V> where K : notnull
    {
        public void WriteSet(K key, V value, JournalStreamWriter writer)
        {
            using var entry = writer.BeginEntry();
            entry.Writer.GetSpan(1)[0] = 1;
            entry.Writer.Advance(1);
            throw new InvalidOperationException("Expected test exception.");
        }

        public void WriteRemove(K key, JournalStreamWriter writer) => throw new NotSupportedException();

        public void WriteClear(JournalStreamWriter writer) => throw new NotSupportedException();

        public void WriteSnapshot(IReadOnlyCollection<KeyValuePair<K, V>> items, JournalStreamWriter writer) => throw new NotSupportedException();

        public void Apply(JournalBufferReader input, IDurableDictionaryCommandHandler<K, V> consumer) => throw new NotSupportedException();
    }

    private sealed class ToggleThrowingDictionarySetCodec<K, V>(IDurableDictionaryCommandCodec<K, V> inner) : IDurableDictionaryCommandCodec<K, V>
        where K : notnull
    {
        public bool ThrowOnSet { get; set; }

        public void WriteSet(K key, V value, JournalStreamWriter writer)
        {
            if (!ThrowOnSet)
            {
                inner.WriteSet(key, value, writer);
                return;
            }

            using var entry = writer.BeginEntry();
            entry.Writer.GetSpan(1)[0] = 1;
            entry.Writer.Advance(1);
            throw new InvalidOperationException("Expected test exception.");
        }

        public void WriteRemove(K key, JournalStreamWriter writer) => inner.WriteRemove(key, writer);

        public void WriteClear(JournalStreamWriter writer) => inner.WriteClear(writer);

        public void WriteSnapshot(IReadOnlyCollection<KeyValuePair<K, V>> items, JournalStreamWriter writer) => inner.WriteSnapshot(items, writer);

        public void Apply(JournalBufferReader input, IDurableDictionaryCommandHandler<K, V> consumer) => inner.Apply(input, consumer);
    }
}
