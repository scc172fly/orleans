using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;
using Orleans.Storage;
using Assert = Orleans.Persistence.TestKit.StorageTestAssertions;
using Record = Orleans.Persistence.TestKit.StorageTestExecution;

namespace Orleans.Persistence.TestKit;

/// <summary>
/// 用于测试 IGrainStorage 实现的基类。
/// </summary>
/// <remarks>
/// 此类提供一套完整的测试，用于验证 IGrainStorage 提供程序
/// 是否符合预期行为。继承此类并实现抽象
/// 方法，即可测试自定义存储提供程序。
/// </remarks>
public abstract class GrainStorageTestRunner
{
    /// <summary>
    /// 初始化 <see cref="GrainStorageTestRunner"/> 类的新实例。
    /// </summary>
    /// <param name="storage">要测试的存储提供程序。</param>
    protected GrainStorageTestRunner(IGrainStorage storage)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// 获取待测试的存储提供程序。
    /// </summary>
    protected IGrainStorage Storage { get; }

    /// <summary>
    /// 创建一对新的 Grain ID 和 Grain 状态供测试使用。
    /// </summary>
    /// <param name="grainId">Grain ID 值。</param>
    /// <param name="version">状态的初始版本。</param>
    /// <returns>Grain ID 与 Grain 状态组成的二元组。</returns>
    protected (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(long grainId, string? version)
    {
        var id = GrainId.Create(GrainType.Create("my-grain-type"), GrainIdKeyExtensions.CreateIntegerKey(grainId));
        var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
        return (id, grainState);
    }

    /// <summary>
    /// 创建一对新的 Grain ID 和 Grain 状态供测试使用。
    /// </summary>
    /// <param name="grainId">Grain ID 值。</param>
    /// <param name="version">状态的初始版本。</param>
    /// <returns>Grain ID 与 Grain 状态组成的二元组。</returns>
    protected (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(string grainId, string? version)
    {
        var id = GrainId.Create("my-grain-type", grainId);
        var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
        return (id, grainState);
    }

    /// <summary>
    /// 写入存储并读回，同时断言版本和状态。
    /// </summary>
    /// <typeparam name="T">Grain 状态类型。</typeparam>
    /// <param name="grainTypeName">Grain 的类型名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <param name="grainState">要写入的 Grain 状态。</param>
    protected Task Store_WriteRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new() =>
        Store_WriteRead(grainTypeName, grainId, grainState, CancellationToken.None);

    /// <inheritdoc cref="Store_WriteRead{T}(string, GrainId, GrainState{T})"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 <see langword="null"/>。</exception>
    protected async Task Store_WriteRead<T>(
        string grainTypeName,
        GrainId grainId,
        GrainState<T> grainState,
        CancellationToken cancellationToken)
        where T : new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grainState);

        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var storedGrainState = new GrainState<T> { State = new T() };
        await Storage.ReadStateAsync(grainTypeName, grainId, storedGrainState, cancellationToken).ConfigureAwait(false);

        Assert.Equal(grainState.ETag, storedGrainState.ETag);
        Assert.Equal(grainState.State, storedGrainState.State);
        Assert.True(storedGrainState.RecordExists);
    }

    /// <summary>
    /// 写入存储、清除并读回，同时断言版本和状态。
    /// </summary>
    /// <typeparam name="T">Grain 状态类型。</typeparam>
    /// <param name="grainTypeName">Grain 的类型名称。</param>
    /// <param name="grainId">Grain ID。</param>
    /// <param name="grainState">要写入并清除的 Grain 状态。</param>
    protected Task Store_WriteClearRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new() =>
        Store_WriteClearRead(grainTypeName, grainId, grainState, CancellationToken.None);

    /// <inheritdoc cref="Store_WriteClearRead{T}(string, GrainId, GrainState{T})"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 为 <see langword="null"/>。</exception>
    protected async Task Store_WriteClearRead<T>(
        string grainTypeName,
        GrainId grainId,
        GrainState<T> grainState,
        CancellationToken cancellationToken)
        where T : new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grainState);

        // 为构造合法的清除场景，必须在清除之前向存储写入状态。
        // 写入和清除都会更改 ETag，因此二者应不同。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken);
        var writtenStateVersion = grainState.ETag;
        var recordExitsAfterWriting = grainState.RecordExists;
        Assert.True(recordExitsAfterWriting);

        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var clearedStateVersion = grainState.ETag;
        Assert.NotEqual(writtenStateVersion, clearedStateVersion);

        var recordExitsAfterClearing = grainState.RecordExists;
        Assert.False(recordExitsAfterClearing);

        var storedGrainState = new GrainState<T> { State = new T(), ETag = clearedStateVersion };
        await Storage.WriteStateAsync(grainTypeName, grainId, storedGrainState, cancellationToken).ConfigureAwait(false);
        Assert.Equal(storedGrainState.State, Activator.CreateInstance<T>());
        Assert.True(storedGrainState.RecordExists);
    }

    /// <summary>
    /// 测试基本读取和写入操作。
    /// </summary>
    public virtual Task PersistenceStorage_WriteReadIdCyrillic() =>
        PersistenceStorage_WriteReadIdCyrillicAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteReadIdCyrillic()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    public virtual async Task PersistenceStorage_WriteReadIdCyrillicAsync(CancellationToken cancellationToken)
    {
        const string CyrillicValue = "\u041f\u0440\u0438\u0432\u0435\u0442, \u043c\u0438\u0440";
        var grainTypeName = "TestGrain";
        var grainReference = GetTestReferenceAndState(0, null);
        var grainState = grainReference.GrainState;
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = CyrillicValue;
        await Storage.WriteStateAsync(grainTypeName, grainReference.GrainId, grainState, cancellationToken).ConfigureAwait(false);
        var storedGrainState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainReference.GrainId, storedGrainState, cancellationToken).ConfigureAwait(false);
        var storedState = Assert.IsType<TestState1>(storedGrainState.State);

        Assert.Equal(grainState.ETag, storedGrainState.ETag);
        Assert.Equal(state, storedState);
        Assert.Equal(CyrillicValue, storedState.A);
    }

    /// <summary>
    /// 写入存储后，尝试使用 null 作为 ETag 重写相同状态，如同 Grain 刚刚创建一样。
    /// </summary>
    public virtual Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException() =>
        PersistenceStorage_WriteDuplicateFailsWithInconsistentStateExceptionAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateExceptionAsync(
        CancellationToken cancellationToken)
    {
        // 在数据库中为具有随机 ID 的 Grain 准备记录。随后将其状态设为 null，以模拟
        // 先前一次激活已成功写入后再次激活的情形。
        string grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);

        await Store_WriteRead(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        grainState.ETag = null;
        var exception = await Record.ExceptionAsync(
            () => Store_WriteRead(grainTypeName, grainId, grainState, cancellationToken)).ConfigureAwait(false);

        Assert.NotNull(exception);
        Assert.IsAssignableFrom<InconsistentStateException>(exception);
    }

    /// <summary>
    /// 向存储写入已知不一致的状态，并断言将抛出异常。
    /// </summary>
    public virtual Task PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException() =>
        PersistenceStorage_WriteInconsistentFailsWithInconsistentStateExceptionAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteInconsistentFailsWithInconsistentStateExceptionAsync(
        CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        grainState.State = new TestState1 { A = "initial", B = 1, C = 2 };
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var staleETag = grainState.ETag;

        grainState.State = new TestState1 { A = "latest", B = 3, C = 4 };
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        var staleState = new GrainState<TestState1>
        {
            State = new TestState1 { A = "stale", B = 5, C = 6 },
            ETag = staleETag,
            RecordExists = true,
        };

        var exception = await Record.ExceptionAsync(
            () => Storage.WriteStateAsync(grainTypeName, grainId, staleState, cancellationToken)).ConfigureAwait(false);
        Assert.IsAssignableFrom<InconsistentStateException>(exception);

        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        Assert.True(readState.RecordExists);
        Assert.Equal(grainState.State, readState.State);
        Assert.Equal(grainState.ETag, readState.ETag);
    }

    /// <summary>
    /// 测试并行写入和读取，以确保存储提供程序正确处理并发。
    /// </summary>
    public virtual Task PersistenceStorage_WriteReadWriteReadStatesInParallel() =>
        PersistenceStorage_WriteReadWriteReadStatesInParallelAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteReadWriteReadStatesInParallel()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    public virtual Task PersistenceStorage_WriteReadWriteReadStatesInParallelAsync(
        CancellationToken cancellationToken)
    {
        return RunPersistenceStorage_WriteReadWriteReadStatesInParallel("Parallel", 100, cancellationToken);
    }

    /// <summary>
    /// 使用提供的 Grain ID 前缀和 Grain 数量测试并行写入与读取。
    /// </summary>
    /// <param name="prefix">Grain ID 的前缀。</param>
    /// <param name="countOfGrains">参与测试的 Grain 数量。</param>
    protected Task RunPersistenceStorage_WriteReadWriteReadStatesInParallel(string prefix, int countOfGrains) =>
        RunPersistenceStorage_WriteReadWriteReadStatesInParallel(prefix, countOfGrains, CancellationToken.None);

    /// <inheritdoc cref="RunPersistenceStorage_WriteReadWriteReadStatesInParallel(string, int)"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    protected async Task RunPersistenceStorage_WriteReadWriteReadStatesInParallel(
        string prefix,
        int countOfGrains,
        CancellationToken cancellationToken)
    {
        // 在数据写入与读取过程中检查版本号（ETag）是否正确（应发生变化）。
        // 此外，Store_WriteRead 测试也会进行自身的验证。
        var grainTypeName = "TestGrain";
        int StartOfRange = 33900;
        int CountOfRange = countOfGrains;

        // 由于版本为 null，存储提供程序会尝试将此数据作为新状态插入。
        // 如果该类已存在数据，写入会失败，存储提供程序会抛出异常。
        var grainStates = Enumerable.Range(StartOfRange, CountOfRange)
            .Select(i => GetTestReferenceAndState($"{prefix}-{Guid.NewGuid():N}-{i}", null))
            .ToList();

        // 避免第一次写入并行执行，以免 INSERT 死锁给系统带来过大压力。
        foreach (var grainData in grainStates)
        {
            // 进行合理性检查，确保首个版本的状态确实为 null。随后将其存入
            // 数据库并获取新版本。
            var firstVersion = grainData.GrainState.ETag;
            Assert.Null(firstVersion);

            await Store_WriteRead(
                grainTypeName,
                grainData.GrainId,
                grainData.GrainState,
                cancellationToken).ConfigureAwait(false);
            var secondVersion = grainData.GrainState.ETag;
            Assert.NotEqual(firstVersion, secondVersion);
        }

        int MaxNumberOfThreads = Environment.ProcessorCount * 3;

        await Parallel.ForEachAsync(
            grainStates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxNumberOfThreads,
                CancellationToken = cancellationToken,
            },
            async (grainData, ct) =>
        {
            // 此循环连续多次将状态写入数据库，以确保其版本得到正确更新。
            for (int k = 0; k < 10; ++k)
            {
                var versionBefore = grainData.GrainState.ETag;
                await Store_WriteRead(grainTypeName, grainData.GrainId, grainData.GrainState, ct);
                var versionAfter = grainData.GrainState.ETag;
                Assert.NotEqual(versionBefore, versionAfter);
            }
        });
    }

    /// <summary>
    /// 测试读取不存在的状态时返回 RecordExists=false。
    /// </summary>
    public virtual Task PersistenceStorage_ReadNonExistentState() =>
        PersistenceStorage_ReadNonExistentStateAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ReadNonExistentState()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ReadNonExistentStateAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        grainState.State = new TestState1 { A = "stale", B = 1, C = 2 };
        grainState.RecordExists = true;

        await Storage.ReadStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        Assert.False(grainState.RecordExists);
        Assert.Null(grainState.ETag);
        Assert.Equal(new TestState1(), grainState.State);
    }

    /// <summary>
    /// 测试写入、清除和再次写入的循环，确保清除后可以重新写入状态。
    /// </summary>
    public virtual Task PersistenceStorage_WriteClearWrite() =>
        PersistenceStorage_WriteClearWriteAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteClearWrite()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteClearWriteAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "First";
        state.B = 1;

        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var firstETag = grainState.ETag;
        Assert.NotNull(firstETag);
        Assert.True(grainState.RecordExists);

        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var clearedETag = grainState.ETag;
        Assert.NotEqual(firstETag, clearedETag);
        Assert.False(grainState.RecordExists);

        grainState.State = new TestState1 { A = "Second", B = 2 };
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var secondETag = grainState.ETag;
        Assert.NotEqual(clearedETag, secondETag);
        Assert.True(grainState.RecordExists);

        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        var readStateValue = Assert.IsType<TestState1>(readState.State);
        Assert.Equal("Second", readStateValue.A);
        Assert.Equal(2, readStateValue.B);
    }

    /// <summary>
    /// 测试完整的写入、清除和读取循环，包括验证清除后状态是否正确初始化。
    /// 此测试验证清除后的新写入会创建可读回的新状态。
    /// </summary>
    public virtual Task PersistenceStorage_WriteClearRead() =>
        PersistenceStorage_WriteClearReadAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteClearRead()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteClearReadAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "Original";
        state.B = 42;
        state.C = 100;

        // 写入初始状态。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken);
        var writtenStateVersion = grainState.ETag;
        Assert.True(grainState.RecordExists);

        // 清除状态。
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var clearedStateVersion = grainState.ETag;
        Assert.NotEqual(writtenStateVersion, clearedStateVersion);
        Assert.False(grainState.RecordExists);

        // 清除后写入新状态。
        var newState = new GrainState<TestState1> { State = new TestState1(), ETag = clearedStateVersion };
        await Storage.WriteStateAsync(grainTypeName, grainId, newState, cancellationToken).ConfigureAwait(false);
        Assert.Equal(newState.State, Activator.CreateInstance<TestState1>());
        Assert.True(newState.RecordExists);

        // 读回并验证它是默认状态。
        var readBackState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readBackState, cancellationToken).ConfigureAwait(false);
        Assert.True(readBackState.RecordExists);
        var readBackStateValue = Assert.IsType<TestState1>(readBackState.State);
        Assert.Null(readBackStateValue.A);
        Assert.Equal(0, readBackStateValue.B);
        Assert.Equal(0L, readBackStateValue.C);
    }

    /// <summary>
    /// 测试使用字符串类型 Grain 键的存储操作。
    /// </summary>
    public virtual Task PersistenceStorage_WriteRead_StringKey() =>
        PersistenceStorage_WriteRead_StringKeyAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteRead_StringKey()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    public virtual async Task PersistenceStorage_WriteRead_StringKeyAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var stringKey = $"StringKey-{Guid.NewGuid()}";
        var (grainId, grainState) = GetTestReferenceAndState(stringKey, null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestString";
        state.B = 123;

        await Store_WriteRead(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 验证可以使用同一个键读回数据。
        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        var readStateValue = Assert.IsType<TestState1>(readState.State);
        Assert.Equal("TestString", readStateValue.A);
        Assert.Equal(123, readStateValue.B);
        Assert.True(readState.RecordExists);
    }

    /// <summary>
    /// 测试使用整数类型 Grain 键的存储操作。
    /// </summary>
    public virtual Task PersistenceStorage_WriteRead_IntegerKey() =>
        PersistenceStorage_WriteRead_IntegerKeyAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteRead_IntegerKey()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteRead_IntegerKeyAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var integerKey = Random.Shared.NextInt64();
        var (grainId, grainState) = GetTestReferenceAndState(integerKey, null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestInteger";
        state.B = 456;
        state.C = integerKey;

        await Store_WriteRead(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 验证可以使用同一个键读回数据。
        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        var readStateValue = Assert.IsType<TestState1>(readState.State);
        Assert.Equal("TestInteger", readStateValue.A);
        Assert.Equal(456, readStateValue.B);
        Assert.Equal(integerKey, readStateValue.C);
        Assert.True(readState.RecordExists);
    }

    /// <summary>
    /// 测试连续写入时 ETag 是否正确更新。
    /// </summary>
    public virtual Task PersistenceStorage_ETagChangesOnWrite() =>
        PersistenceStorage_ETagChangesOnWriteAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ETagChangesOnWrite()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ETagChangesOnWriteAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);

        // 第一次写入。
        state.A = "Version1";
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag1 = grainState.ETag;
        Assert.NotNull(etag1);

        // 第二次写入。
        state.A = "Version2";
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag2 = grainState.ETag;
        Assert.NotNull(etag2);
        Assert.NotEqual(etag1, etag2);

        // 第三次写入。
        state.A = "Version3";
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag3 = grainState.ETag;
        Assert.NotNull(etag3);
        Assert.NotEqual(etag2, etag3);
        Assert.NotEqual(etag1, etag3);
    }

    /// <summary>
    /// 测试在任何写入之前调用 Clear 是否可以正常工作。
    /// 验证清除不存在的状态后，RecordExists 为 false 且 State 不为 null。
    /// </summary>
    public virtual Task PersistenceStorage_ClearBeforeWrite() =>
        PersistenceStorage_ClearBeforeWriteAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearBeforeWrite()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ClearBeforeWriteAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        grainState.State = new TestState1 { A = "stale", B = 1, C = 2 };
        grainState.RecordExists = true;

        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        Assert.False(grainState.RecordExists);
        Assert.Equal(new TestState1(), grainState.State);
    }

    /// <summary>
    /// 测试 Clear 操作后 State 属性始终不为 null。
    /// 验证即使清除之后，State 对象也仍处于已初始化状态。
    /// </summary>
    public virtual Task PersistenceStorage_ClearStateDoesNotNullifyState() =>
        PersistenceStorage_ClearStateDoesNotNullifyStateAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearStateDoesNotNullifyState()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ClearStateDoesNotNullifyStateAsync(
        CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestData";
        state.B = 100;

        // 写入后清除。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.True(grainState.RecordExists);
        Assert.NotNull(grainState.State);

        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 即使清除后，State 对象也不应为 null。
        Assert.NotNull(grainState.State);
        Assert.False(grainState.RecordExists);
    }

    /// <summary>
    /// 测试 Clear 操作后 ETag 是否变化。
    /// 某些提供程序可能在清除时将 ETag 设为 null，其他提供程序可能更新它，这两种情况均可接受。
    /// </summary>
    public virtual Task PersistenceStorage_ClearUpdatesETag() =>
        PersistenceStorage_ClearUpdatesETagAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearUpdatesETag()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ClearUpdatesETagAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestData";

        // 写入状态。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var writeETag = grainState.ETag;
        Assert.NotNull(writeETag);

        // 清除状态。
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var clearETag = grainState.ETag;

        // ETag 应已变化（变为 null 或新值）。
        Assert.NotEqual(writeETag, clearETag);
        Assert.False(grainState.RecordExists);
    }

    /// <summary>
    /// 测试状态被清除后的读取操作。
    /// 验证读取已清除状态时返回 RecordExists=false 且 State 不为 null。
    /// </summary>
    public virtual Task PersistenceStorage_ReadAfterClear() =>
        PersistenceStorage_ReadAfterClearAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ReadAfterClear()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ReadAfterClearAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "OriginalData";
        state.B = 42;

        // 写入、清除，再读取。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 读取已清除的状态。
        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);

        // 读取已清除的状态后。
        Assert.False(readState.RecordExists);
        Assert.NotNull(readState.State); // State 仍应处于已初始化状态。
    }

    /// <summary>
    /// 测试多次连续清除操作。
    /// 验证清除已经清除的状态是幂等操作。
    /// </summary>
    public virtual Task PersistenceStorage_MultipleClearOperations() =>
        PersistenceStorage_MultipleClearOperationsAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_MultipleClearOperations()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_MultipleClearOperationsAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestData";

        // 写入状态。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.True(grainState.RecordExists);

        // 第一次清除。
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var firstClearETag = grainState.ETag;
        Assert.False(grainState.RecordExists);

        // 第二次清除，应具有幂等性。
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.False(grainState.RecordExists);
        Assert.NotNull(grainState.State);
    }

    /// <summary>
    /// 测试读取不存在的状态后，State 属性是否仍处于已初始化状态（不为 null）。
    /// </summary>
    public virtual Task PersistenceStorage_ReadNonExistentStateHasNonNullState() =>
        PersistenceStorage_ReadNonExistentStateHasNonNullStateAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ReadNonExistentStateHasNonNullState()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ReadNonExistentStateHasNonNullStateAsync(
        CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);

        // 读取从未写入的状态。
        await Storage.ReadStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 即使没有写入任何数据，State 也应处于已初始化状态。
        Assert.False(grainState.RecordExists);
        Assert.NotNull(grainState.State);
        Assert.Null(grainState.ETag);
    }

    /// <summary>
    /// 测试即使 Grain ID 相同，不同状态名称也能标识独立记录。
    /// </summary>
    public virtual Task PersistenceStorage_StateNamesUseIndependentRecords() =>
        PersistenceStorage_StateNamesUseIndependentRecordsAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_StateNamesUseIndependentRecords()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    public virtual async Task PersistenceStorage_StateNamesUseIndependentRecordsAsync(
        CancellationToken cancellationToken)
    {
        var grainId = GrainId.Create("test-grain", Guid.NewGuid().ToString("N"));
        var first = new GrainState<TestState1> { State = new TestState1 { A = "first", B = 1, C = 2 } };
        var second = new GrainState<TestState1> { State = new TestState1 { A = "second", B = 3, C = 4 } };

        await Storage.WriteStateAsync("first-state", grainId, first, cancellationToken).ConfigureAwait(false);
        await Storage.WriteStateAsync("second-state", grainId, second, cancellationToken).ConfigureAwait(false);

        var firstRead = new GrainState<TestState1> { State = new TestState1() };
        var secondRead = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync("first-state", grainId, firstRead, cancellationToken).ConfigureAwait(false);
        await Storage.ReadStateAsync("second-state", grainId, secondRead, cancellationToken).ConfigureAwait(false);

        Assert.Equal(first.State, firstRead.State);
        Assert.Equal(second.State, secondRead.State);
        Assert.NotEqual(firstRead.State, secondRead.State);
    }

    /// <summary>
    /// 测试使用过期 ETag 清除状态时是否抛出 <see cref="InconsistentStateException"/>，
    /// 并保留最新的已存储状态。
    /// </summary>
    public virtual Task PersistenceStorage_ClearInconsistentFailsWithInconsistentStateException() =>
        PersistenceStorage_ClearInconsistentFailsWithInconsistentStateExceptionAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearInconsistentFailsWithInconsistentStateException()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ClearInconsistentFailsWithInconsistentStateExceptionAsync(
        CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        grainState.State = new TestState1 { A = "initial", B = 1, C = 2 };
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var staleETag = grainState.ETag;

        grainState.State = new TestState1 { A = "latest", B = 3, C = 4 };
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        var staleState = new GrainState<TestState1>
        {
            State = new TestState1(),
            ETag = staleETag,
            RecordExists = true,
        };

        var exception = await Record.ExceptionAsync(
            () => Storage.ClearStateAsync(grainTypeName, grainId, staleState, cancellationToken)).ConfigureAwait(false);
        Assert.IsAssignableFrom<InconsistentStateException>(exception);

        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        Assert.True(readState.RecordExists);
        Assert.Equal(grainState.State, readState.State);
        Assert.Equal(grainState.ETag, readState.ETag);

    }

    /// <summary>
    /// 测试写入、读取、清除、再次读取的循环，以验证状态转换。
    /// </summary>
    public virtual Task PersistenceStorage_WriteReadClearReadCycle() =>
        PersistenceStorage_WriteReadClearReadCycleAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteReadClearReadCycle()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteReadClearReadCycleAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "InitialValue";
        state.B = 99;

        // 写入。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var writeETag = grainState.ETag;
        Assert.True(grainState.RecordExists);

        // 读回。
        var readState1 = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState1, cancellationToken).ConfigureAwait(false);
        Assert.True(readState1.RecordExists);
        var readStateValue = Assert.IsType<TestState1>(readState1.State);
        Assert.Equal("InitialValue", readStateValue.A);
        Assert.Equal(99, readStateValue.B);
        Assert.Equal(writeETag, readState1.ETag);

        // 清除。
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.False(grainState.RecordExists);

        // 清除后读取。
        var readState2 = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState2, cancellationToken).ConfigureAwait(false);
        Assert.False(readState2.RecordExists);
        Assert.NotNull(readState2.State);
    }

    /// <summary>
    /// 测试即使使用相同的值更新状态，ETag 仍会更新。
    /// </summary>
    public virtual Task PersistenceStorage_WriteWithSameValuesUpdatesETag() =>
        PersistenceStorage_WriteWithSameValuesUpdatesETagAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteWithSameValuesUpdatesETag()"/>
    /// <param name="cancellationToken">用于取消存储操作的令牌。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteWithSameValuesUpdatesETagAsync(
        CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "SameValue";
        state.B = 123;

        // 第一次写入。
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag1 = grainState.ETag;
        Assert.NotNull(etag1);

        // 使用相同的值再次写入。
        state.A = "SameValue";
        state.B = 123;
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag2 = grainState.ETag;
        Assert.NotNull(etag2);

        // 即使值相同，ETag 仍应变化。
        Assert.NotEqual(etag1, etag2);
    }
}
