using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;
using Orleans.Storage;
using Assert = Orleans.Persistence.TestKit.StorageTestAssertions;
using Record = Orleans.Persistence.TestKit.StorageTestExecution;

namespace Orleans.Persistence.TestKit;

/// <summary>
/// 测试 IGrainStorage 实施的基础等级。
/// </summary>
/// <remarks>
/// 此类为核实 IGrainStorage 提供者提供了一个全面的测试套件
/// 与预期的行为相符。 从这个班级继承遗产, 并应用抽象
/// 测试自定义存储提供者的方法。
/// </remarks>
public abstract class GrainStorageTestRunner
{
    /// <summary>
    /// 初始化 <see cref="GrainStorageTestRunner"/> 类的新实例。
    /// </summary>
    /// <param name="storage">用于测试的存储提供者。</param>
    protected GrainStorageTestRunner(IGrainStorage storage)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// 测试储物供应商
    /// </summary>
    protected IGrainStorage Storage { get; }

    /// <summary>
    /// 创建用于测试的新 grain ID和 grain 状态对。
    /// </summary>
    /// <param name="grainId">下 grain 身份识别价值。</param>
    /// <param name="version">最初的州立版本</param>
    /// <returns>A grain 和身份证和身份证 grain 状态对夫妇。</returns>
    protected (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(long grainId, string? version)
    {
        var id = GrainId.Create(GrainType.Create("my-grain-type"), GrainIdKeyExtensions.CreateIntegerKey(grainId));
        var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
        return (id, grainState);
    }

    /// <summary>
    /// 创建用于测试的新 grain ID和 grain 状态对。
    /// </summary>
    /// <param name="grainId">下 grain 身份识别价值。</param>
    /// <param name="version">最初的州立版本</param>
    /// <returns>A grain 和身份证和身份证 grain 状态对夫妇。</returns>
    protected (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(string grainId, string? version)
    {
        var id = GrainId.Create("my-grain-type", grainId);
        var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
        return (id, grainState);
    }

    /// <summary>
    /// 写到存储和读回, 既维护版本又维护国家。
    /// </summary>
    /// <typeparam name="T"> grain 状态类型。</typeparam>
    /// <param name="grainTypeName">类型 grain.</param>
    /// <param name="grainId">下 grain ID.</param>
    /// <param name="grainState">写入的 grain 状态。</param>
    protected Task Store_WriteRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new() =>
        Store_WriteRead(grainTypeName, grainId, grainState, CancellationToken.None);

    /// <inheritdoc cref="Store_WriteRead{T}(string, GrainId, GrainState{T})"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 是 <see langword="null"/>.</exception>
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
    /// 写到存储,清空和背面读, 既主张版本又主张州。
    /// </summary>
    /// <typeparam name="T"> grain 状态类型。</typeparam>
    /// <param name="grainTypeName">类型 grain.</param>
    /// <param name="grainId">下 grain ID.</param>
    /// <param name="grainState">下 grain 以状态写清楚。</param>
    protected Task Store_WriteClearRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new() =>
        Store_WriteClearRead(grainTypeName, grainId, grainState, CancellationToken.None);

    /// <inheritdoc cref="Store_WriteClearRead{T}(string, GrainId, GrainState{T})"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    /// <exception cref="ArgumentNullException"><paramref name="grainState"/> 是 <see langword="null"/>.</exception>
    protected async Task Store_WriteClearRead<T>(
        string grainTypeName,
        GrainId grainId,
        GrainState<T> grainState,
        CancellationToken cancellationToken)
        where T : new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grainState);

        // 清理的法律状况必须先向储存国书面说明,然后才能清理。
        // 书写和清除两者都改变ETag,因此它们应该有所不同。
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
    /// 测试基本读写操作。
    /// </summary>
    public virtual Task PersistenceStorage_WriteReadIdCyrillic() =>
        PersistenceStorage_WriteReadIdCyrillicAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteReadIdCyrillic()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 写入存储文件并尝试用 NULL 重写同一状态, 仿佛 grain 是刚刚创建的 。
    /// </summary>
    public virtual Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException() =>
        PersistenceStorage_WriteDuplicateFailsWithInconsistentStateExceptionAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteDuplicateFailsWithInconsistentStateExceptionAsync(
        CancellationToken cancellationToken)
    {
        // A grain 数据库将设置随机身份标识。 然后其状态将被设为无效以模拟
        // 和它就像一个成功写作的 之后的第二次激活。
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
    /// 将提出一个例外。
    /// </summary>
    public virtual Task PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException() =>
        PersistenceStorage_WriteInconsistentFailsWithInconsistentStateExceptionAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 测试平行写作和阅读以确保提供者正确处理同值货币。
    /// </summary>
    public virtual Task PersistenceStorage_WriteReadWriteReadStatesInParallel() =>
        PersistenceStorage_WriteReadWriteReadStatesInParallelAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteReadWriteReadStatesInParallel()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    public virtual Task PersistenceStorage_WriteReadWriteReadStatesInParallelAsync(
        CancellationToken cancellationToken)
    {
        return RunPersistenceStorage_WriteReadWriteReadStatesInParallel("Parallel", 100, cancellationToken);
    }

    /// <summary>
    /// 测试平行写作和读取时使用提供的 grain ID 前缀和 grain 计数。
    /// </summary>
    /// <param name="prefix"> grain ID的前缀。</param>
    /// <param name="countOfGrains">测试的 grains 号。</param>
    protected Task RunPersistenceStorage_WriteReadWriteReadStatesInParallel(string prefix, int countOfGrains) =>
        RunPersistenceStorage_WriteReadWriteReadStatesInParallel(prefix, countOfGrains, CancellationToken.None);

    /// <inheritdoc cref="RunPersistenceStorage_WriteReadWriteReadStatesInParallel(string, int)"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    protected async Task RunPersistenceStorage_WriteReadWriteReadStatesInParallel(
        string prefix,
        int countOfGrains,
        CancellationToken cancellationToken)
    {
        // 数据是写成的,读取版本号(ETags)后会检查是否正确(它们会改变)。
        // 此外,存储WriteRead测试也验证了这些测试。
        var grainTypeName = "TestGrain";
        int StartOfRange = 33900;
        int CountOfRange = countOfGrains;

        // 由于版本为 NULL, 存储供应商试图将数据插入新状态 。
        // 如果已有此类数据, 则写入失败, 存储提供者丢弃 。
        var grainStates = Enumerable.Range(StartOfRange, CountOfRange)
            .Select(i => GetTestReferenceAndState($"{prefix}-{Guid.NewGuid():N}-{i}", null))
            .ToList();

        // 避免第一篇文章的平行化,以免因科学、社会、环境和专家审评组陷入僵局而使系统失去压力
        foreach (var grainData in grainStates)
        {
            // 一种精神检查器, 其第一版本的状态是无效的。 然后它被存储
            // 输入数据库并获得新版本。
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
            // 此循环将连续的状态写入数据库, 以确保数据库版本能适当更新 。
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
    /// 测试读取不存在状态返回 RecordExists=false。
    /// </summary>
    public virtual Task PersistenceStorage_ReadNonExistentState() =>
        PersistenceStorage_ReadNonExistentStateAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ReadNonExistentState()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 测试写、清楚和写周期,以确保在清理后可以重新写出状态。
    /// </summary>
    public virtual Task PersistenceStorage_WriteClearWrite() =>
        PersistenceStorage_WriteClearWriteAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteClearWrite()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 测试完整的写清读周期,包括核查清楚后是否正确初始化。
    /// 测试证实清理后, 新写作会创造一个新的状态, 可以读回 。
    /// </summary>
    public virtual Task PersistenceStorage_WriteClearRead() =>
        PersistenceStorage_WriteClearReadAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteClearRead()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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

        // 写初始状态
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken);
        var writtenStateVersion = grainState.ETag;
        Assert.True(grainState.RecordExists);

        // 清除状态
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var clearedStateVersion = grainState.ETag;
        Assert.NotEqual(writtenStateVersion, clearedStateVersion);
        Assert.False(grainState.RecordExists);

        // 清清楚后写入新状态
        var newState = new GrainState<TestState1> { State = new TestState1(), ETag = clearedStateVersion };
        await Storage.WriteStateAsync(grainTypeName, grainId, newState, cancellationToken).ConfigureAwait(false);
        Assert.Equal(newState.State, Activator.CreateInstance<TestState1>());
        Assert.True(newState.RecordExists);

        // 读回去确认它是否为默认状态
        var readBackState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readBackState, cancellationToken).ConfigureAwait(false);
        Assert.True(readBackState.RecordExists);
        var readBackStateValue = Assert.IsType<TestState1>(readBackState.State);
        Assert.Null(readBackStateValue.A);
        Assert.Equal(0, readBackStateValue.B);
        Assert.Equal(0L, readBackStateValue.C);
    }

    /// <summary>
    /// 使用字符串 grain 键测试存储操作。
    /// </summary>
    public virtual Task PersistenceStorage_WriteRead_StringKey() =>
        PersistenceStorage_WriteRead_StringKeyAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteRead_StringKey()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    public virtual async Task PersistenceStorage_WriteRead_StringKeyAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var stringKey = $"StringKey-{Guid.NewGuid()}";
        var (grainId, grainState) = GetTestReferenceAndState(stringKey, null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestString";
        state.B = 123;

        await Store_WriteRead(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 校验后,我们用同一密钥可以读回
        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        var readStateValue = Assert.IsType<TestState1>(readState.State);
        Assert.Equal("TestString", readStateValue.A);
        Assert.Equal(123, readStateValue.B);
        Assert.True(readState.RecordExists);
    }

    /// <summary>
    /// 使用整数 grain 键测试存储操作。
    /// </summary>
    public virtual Task PersistenceStorage_WriteRead_IntegerKey() =>
        PersistenceStorage_WriteRead_IntegerKeyAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteRead_IntegerKey()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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

        // 校验后,我们用同一密钥可以读回
        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);
        var readStateValue = Assert.IsType<TestState1>(readState.State);
        Assert.Equal("TestInteger", readStateValue.A);
        Assert.Equal(456, readStateValue.B);
        Assert.Equal(integerKey, readStateValue.C);
        Assert.True(readState.RecordExists);
    }

    /// <summary>
    /// 测试ETag在连续写作时正确更新。
    /// </summary>
    public virtual Task PersistenceStorage_ETagChangesOnWrite() =>
        PersistenceStorage_ETagChangesOnWriteAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ETagChangesOnWrite()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ETagChangesOnWriteAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);

        // 首写
        state.A = "Version1";
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag1 = grainState.ETag;
        Assert.NotNull(etag1);

        // 第二书第二稿
        state.A = "Version2";
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag2 = grainState.ETag;
        Assert.NotNull(etag2);
        Assert.NotEqual(etag1, etag2);

        // 第三写第三书
        state.A = "Version3";
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag3 = grainState.ETag;
        Assert.NotNull(etag3);
        Assert.NotEqual(etag2, etag3);
        Assert.NotEqual(etag1, etag3);
    }

    /// <summary>
    /// 测试在任何写入工作正确之前拨打清除 。
    /// 证明 RecordExists 是假的,在清理不存在的国家之后国家并非无效。
    /// </summary>
    public virtual Task PersistenceStorage_ClearBeforeWrite() =>
        PersistenceStorage_ClearBeforeWriteAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearBeforeWrite()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 测试在清除后国家财产从不无效。
    /// 即使在清理后,国家物体仍继续初始化。
    /// </summary>
    public virtual Task PersistenceStorage_ClearStateDoesNotNullifyState() =>
        PersistenceStorage_ClearStateDoesNotNullifyStateAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearStateDoesNotNullifyState()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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

        // 写完,然后清清楚的
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.True(grainState.RecordExists);
        Assert.NotNull(grainState.State);

        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 国家反对不应无效,即使明确
        Assert.NotNull(grainState.State);
        Assert.False(grainState.RecordExists);
    }

    /// <summary>
    /// 测试ETag在清除操作后更改。
    /// 有些供应商可能将ETag定为无效,另一些供应商则可能更新-两者都可接受。
    /// </summary>
    public virtual Task PersistenceStorage_ClearUpdatesETag() =>
        PersistenceStorage_ClearUpdatesETagAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearUpdatesETag()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ClearUpdatesETagAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestData";

        // 写状态
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var writeETag = grainState.ETag;
        Assert.NotNull(writeETag);

        // 清状态
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var clearETag = grainState.ETag;

        // ETag应该改变(要么变为无效,要么成为新值)
        Assert.NotEqual(writeETag, clearETag);
        Assert.False(grainState.RecordExists);
    }

    /// <summary>
    /// 测试经清理后读取状态。
    /// 证明读取已清除状态的返回 RecordExists=假国并非无效。
    /// </summary>
    public virtual Task PersistenceStorage_ReadAfterClear() =>
        PersistenceStorage_ReadAfterClearAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ReadAfterClear()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ReadAfterClearAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "OriginalData";
        state.B = 42;

        // 写,清楚,然后读
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 读取清除状态
        var readState = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState, cancellationToken).ConfigureAwait(false);

        // 阅读了经清理的状态
        Assert.False(readState.RecordExists);
        Assert.NotNull(readState.State); // 国家仍应启动
    }

    /// <summary>
    /// 测试多次连续清除操作。
    /// 证明一个已经批准的国家是无能的
    /// </summary>
    public virtual Task PersistenceStorage_MultipleClearOperations() =>
        PersistenceStorage_MultipleClearOperationsAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_MultipleClearOperations()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_MultipleClearOperationsAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "TestData";

        // 写状态
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.True(grainState.RecordExists);

        // 第一次清楚
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var firstClearETag = grainState.ETag;
        Assert.False(grainState.RecordExists);

        // 第二清楚,应该是无能者
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.False(grainState.RecordExists);
        Assert.NotNull(grainState.State);
    }

    /// <summary>
    /// 检验国家财产在读完之后是否仍然初始化(并非无效)。
    /// </summary>
    public virtual Task PersistenceStorage_ReadNonExistentStateHasNonNullState() =>
        PersistenceStorage_ReadNonExistentStateHasNonNullStateAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ReadNonExistentStateHasNonNullState()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_ReadNonExistentStateHasNonNullStateAsync(
        CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);

        // 读状态从未写过
        await Storage.ReadStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);

        // 即使没有书面文字,国家也应启动
        Assert.False(grainState.RecordExists);
        Assert.NotNull(grainState.State);
        Assert.Null(grainState.ETag);
    }

    /// <summary>
    /// 测试国名是否识别独立记录,即使 grain ID相同。
    /// </summary>
    public virtual Task PersistenceStorage_StateNamesUseIndependentRecords() =>
        PersistenceStorage_StateNamesUseIndependentRecordsAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_StateNamesUseIndependentRecords()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 以平坦的ETag抛弃清除状态测试 <see cref="InconsistentStateException"/>
    /// 保存最新储存状态。
    /// </summary>
    public virtual Task PersistenceStorage_ClearInconsistentFailsWithInconsistentStateException() =>
        PersistenceStorage_ClearInconsistentFailsWithInconsistentStateExceptionAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_ClearInconsistentFailsWithInconsistentStateException()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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
    /// 测试写读清除周期,以核实各州的过渡情况。
    /// </summary>
    public virtual Task PersistenceStorage_WriteReadClearReadCycle() =>
        PersistenceStorage_WriteReadClearReadCycleAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteReadClearReadCycle()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Randomness selects test-data grain identifiers for storage conformance scenarios.")]
    public virtual async Task PersistenceStorage_WriteReadClearReadCycleAsync(CancellationToken cancellationToken)
    {
        var grainTypeName = "TestGrain";
        var (grainId, grainState) = GetTestReferenceAndState(Random.Shared.NextInt64(), null);
        var state = Assert.IsType<TestState1>(grainState.State);
        state.A = "InitialValue";
        state.B = 99;

        // 写写
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var writeETag = grainState.ETag;
        Assert.True(grainState.RecordExists);

        // 回读
        var readState1 = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState1, cancellationToken).ConfigureAwait(false);
        Assert.True(readState1.RecordExists);
        var readStateValue = Assert.IsType<TestState1>(readState1.State);
        Assert.Equal("InitialValue", readStateValue.A);
        Assert.Equal(99, readStateValue.B);
        Assert.Equal(writeETag, readState1.ETag);

        // 清除
        await Storage.ClearStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        Assert.False(grainState.RecordExists);

        // 读完后清楚
        var readState2 = new GrainState<TestState1> { State = new TestState1() };
        await Storage.ReadStateAsync(grainTypeName, grainId, readState2, cancellationToken).ConfigureAwait(false);
        Assert.False(readState2.RecordExists);
        Assert.NotNull(readState2.State);
    }

    /// <summary>
    /// 测试以相同值更新状态的测试仍然在更新ETag。
    /// </summary>
    public virtual Task PersistenceStorage_WriteWithSameValuesUpdatesETag() =>
        PersistenceStorage_WriteWithSameValuesUpdatesETagAsync(CancellationToken.None);

    /// <inheritdoc cref="PersistenceStorage_WriteWithSameValuesUpdatesETag()"/>
    /// <param name="cancellationToken">取消存储操作的符号 。</param>
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

        // 首写
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag1 = grainState.ETag;
        Assert.NotNull(etag1);

        // 以相同的值再次写入
        state.A = "SameValue";
        state.B = 123;
        await Storage.WriteStateAsync(grainTypeName, grainId, grainState, cancellationToken).ConfigureAwait(false);
        var etag2 = grainState.ETag;
        Assert.NotNull(etag2);

        // ETag仍应改变,即使数值相同
        Assert.NotEqual(etag1, etag2);
    }
}
