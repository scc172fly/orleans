using System.Globalization;
using Orleans.Runtime;
using Orleans.Storage;
using UnitTests.StorageTests.Relational.TestDataSets;
using Xunit;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 包含适用于任意后端的断言的存储测试。
    /// </summary>
    /// <remarks>
    /// 此类不属于继承层次结构，以便更清晰地分离各种框架
    /// 代码，甚至可以测试测试本身。测试使用唯一的新标识（Guid），因此在所有存储测试
    /// 运行完毕之前无需清理存储。
    /// </remarks>
    internal class CommonStorageTests
    {
        public CommonStorageTests(IGrainStorage storage) => Storage = storage ?? throw new ArgumentNullException(nameof(storage));

        /// <summary>
        /// 被测试的存储提供程序。
        /// </summary>
        public IGrainStorage Storage { get; }

        /// <summary>
        /// 创建新的 Grain 与 Grain 引用对。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="version">状态的初始版本。</param>
        /// <returns>由 Grain 引用和状态组成的二元组。（译注：此处实际返回 GrainId 与 GrainState。）</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0022")]
        internal (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(long grainId, string? version)
        {
            var id = GrainId.Create(GrainType.Create("my-grain-type"), GrainIdKeyExtensions.CreateIntegerKey(grainId));
            var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
            return (id, grainState);
        }

        /// <summary>
        /// 创建新的 Grain 与 Grain 引用对。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="version">状态的初始版本。</param>
        /// <returns>由 Grain 引用和状态组成的二元组。（译注：此处实际返回 GrainId 与 GrainState。）</returns>
        internal (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(string grainId, string? version)
        {
            var id = GrainId.Create("my-grain-type", grainId);
            var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
            return (id, grainState);
        }

        /// <summary>
        /// 向存储写入已知不一致的状态，并断言将抛出异常。
        /// </summary>
        /// <returns></returns>
        internal async Task PersistenceStorage_Relational_WriteReadIdCyrillic()
        {
            var grainTypeName = GrainTypeGenerator.GetGrainType<Guid>();
            var grainReference = this.GetTestReferenceAndState(0, null);
            var grainState = grainReference.GrainState;
            await Storage.WriteStateAsync(grainTypeName, grainReference.GrainId, grainState).ConfigureAwait(false);
            var storedGrainState = new GrainState<TestState1> { State = new TestState1() };
            await Storage.ReadStateAsync(grainTypeName, grainReference.GrainId, storedGrainState).ConfigureAwait(false);

            Assert.Equal(grainState.ETag, storedGrainState.ETag);
            Assert.Equal(grainState.State, storedGrainState.State);
        }

        /// <summary>
        /// 写入存储，并尝试以 NULL 作为 ETag 重新写入相同状态，就像
        /// Grain 刚刚创建一样。
        /// </summary>
        /// <returns>
        /// 提供程序抛出的 <see cref="InconsistentStateException"/>。可通过存储专用断言
        /// 进一步检查此异常。
        /// </returns>
        internal async Task<InconsistentStateException> PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()
        {
            //将带随机 ID 的 Grain 安排到数据库中。随后将其状态置为 null，以模拟
            //在一个已成功写入的激活之后出现第二个激活的情况。
            string grainTypeName = GrainTypeGenerator.GetGrainType<Guid>();
            var (grainId, grainState) = this.GetTestReferenceAndState(RandomUtilities.GetRandom<long>(), null);

            await Store_WriteRead(grainTypeName, grainId, grainState).ConfigureAwait(false);
            grainState.ETag = null;
            var exception = await Record.ExceptionAsync(() => Store_WriteRead(grainTypeName, grainId, grainState)).ConfigureAwait(false);

            Assert.NotNull(exception);
            Assert.IsType<InconsistentStateException>(exception);

            return (InconsistentStateException)exception;
        }

        /// <summary>
        /// 向存储写入已知不一致的状态，并断言将抛出异常。
        /// </summary>
        /// <returns>
        /// 提供程序抛出的 <see cref="InconsistentStateException"/>。可通过存储专用断言
        /// 进一步检查此异常。
        /// </returns>
        internal async Task<InconsistentStateException> PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException()
        {
            //对于此类型和 ID，预期存储中不存在的某个版本。
            var inconsistentStateVersion = RandomUtilities.GetRandom<int>().ToString(CultureInfo.InvariantCulture);

            var inconsistentState = this.GetTestReferenceAndState(RandomUtilities.GetRandom<long>(), inconsistentStateVersion);
            string grainTypeName = GrainTypeGenerator.GetGrainType<Guid>();
            var exception = await Record.ExceptionAsync(() => Store_WriteRead(grainTypeName, inconsistentState.GrainId, inconsistentState.GrainState)).ConfigureAwait(false);

            Assert.NotNull(exception);
            Assert.IsType<InconsistentStateException>(exception);

            return (InconsistentStateException)exception;
        }

        internal async Task PersistenceStorage_WriteReadWriteReadStatesInParallel(string prefix = nameof(this.PersistenceStorage_WriteReadWriteReadStatesInParallel), int countOfGrains = 100)
        {
            //在写入和读取数据时，检查版本号（ETag）是否正确（它们会发生变化）。
            //此外，Store_WriteRead 测试会执行自身的验证。
            var grainTypeName = GrainTypeGenerator.GetGrainType<Guid>();
            int StartOfRange = 33900;
            int CountOfRange = countOfGrains;

            //由于版本为 NULL，存储提供程序尝试插入该数据
            //作为新状态。若此类的数据已经存在，写入将失败，
            //存储提供程序将抛出异常。这实质上意味着此范围
            //选择不当，或测试因其他问题而失败。
            var grainStates = Enumerable.Range(StartOfRange, CountOfRange).Select(i => GetTestReferenceAndState($"{prefix}-{Guid.NewGuid():N}-{i}", null)).ToList();

            // 避免并行执行首次写入，以免 INSERT 上的死锁
            // 给系统造成过大压力。
            foreach (var grainData in grainStates)
            {
                //进行基本合理性检查，确认第一个版本的状态确实为 null。随后将其存入
                //数据库并取得新的版本。
                var firstVersion = grainData.GrainState.ETag;
                Assert.Null(firstVersion);

                await Store_WriteRead(grainTypeName, grainData.GrainId, grainData.GrainState).ConfigureAwait(false);
                var secondVersion = grainData.GrainState.ETag;
                Assert.NotEqual(firstVersion, secondVersion);
            }
            ;

            int MaxNumberOfThreads = Environment.ProcessorCount * 3;
            // Parallel.ForEachAsync 的目的是确保从多个线程并发测试存储提供程序，
            // 与实际运行系统的情况相同。不过，
            // 应限制并行度（并发线程数），以避免不必要地
            // 使线程池饥饿并扩容（扩容非常缓慢），如果少量线程结合
            // 任务级并行即可触发大多数并发场景。

            await Parallel.ForEachAsync(grainStates, new ParallelOptions { MaxDegreeOfParallelism = MaxNumberOfThreads }, async (grainData, ct) =>
            {
                // 此循环连续多次将状态写入数据库，以确保其
                // 版本得到正确更新。
                for (int k = 0; k < 10; ++k)
                {
                    var versionBefore = grainData.GrainState.ETag;
                    await RetryHelper.RetryOnExceptionAsync(5, RetryOperation.Sigmoid, async () =>
                    {
                        await Store_WriteRead(grainTypeName, grainData.GrainId, grainData.GrainState);
                        return 0;
                    }, ct);

                    var versionAfter = grainData.GrainState.ETag;
                    Assert.NotEqual(versionBefore, versionAfter);
                }
            });
        }

        /// <summary>
        /// 写入存储、清除并读回，断言版本和状态均符合预期。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="grainTypeName">Grain 的类型名称。</param>
        /// <param name="grainReference">与 Orleans 提供的形式相同的 Grain 引用。（译注：原注释使用 grainReference，当前方法的对应参数为 grainId。）</param>
        /// <param name="grainState">Grain 所持有且 Orleans 所传递的 Grain 状态。</param>
        /// <returns></returns>
        internal async Task Store_WriteClearRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new()
        {
            //必须在清除之前先向存储写入状态，以构造合法的清除场景。
            //写入和清除均改变 ETag，因此它们应不相同。
            await Storage.WriteStateAsync(grainTypeName, grainId, grainState);
            var writtenStateVersion = grainState.ETag;
            var recordExitsAfterWriting = grainState.RecordExists;
            Assert.True(recordExitsAfterWriting);

            await Storage.ClearStateAsync(grainTypeName, grainId, grainState).ConfigureAwait(false);
            var clearedStateVersion = grainState.ETag;
            Assert.NotEqual(writtenStateVersion, clearedStateVersion);

            var recordExitsAfterClearing = grainState.RecordExists;
            Assert.False(recordExitsAfterClearing);

            var storedGrainState = new GrainState<T> { State = new T(), ETag = clearedStateVersion };
            await Storage.WriteStateAsync(grainTypeName, grainId, storedGrainState).ConfigureAwait(false);
            Assert.Equal(storedGrainState.State, Activator.CreateInstance<T>());
            Assert.True(storedGrainState.RecordExists);
        }

        /// <summary>
        /// 写入存储、读回，并断言版本和状态均符合预期。
        /// </summary>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <param name="grainTypeName">Grain 的类型名称。</param>
        /// <param name="grainReference">与 Orleans 提供的形式相同的 Grain 引用。（译注：原注释使用 grainReference，当前方法的对应参数为 grainId。）</param>
        /// <param name="grainState">Grain 所持有且 Orleans 所传递的 Grain 状态。</param>
        /// <returns></returns>
        internal async Task Store_WriteRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new()
        {
            await Storage.WriteStateAsync(grainTypeName, grainId, grainState).ConfigureAwait(false);
            var storedGrainState = new GrainState<T> { State = new T() };
            await Storage.ReadStateAsync(grainTypeName, grainId, storedGrainState).ConfigureAwait(false);

            Assert.Equal(grainState.ETag, storedGrainState.ETag);
            Assert.Equal(grainState.State, storedGrainState.State);
            Assert.True(storedGrainState.RecordExists);
        }
    }
}
