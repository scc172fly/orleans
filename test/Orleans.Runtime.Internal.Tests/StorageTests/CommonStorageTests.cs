using System.Globalization;
using Orleans.Runtime;
using Orleans.Storage;
using UnitTests.StorageTests.Relational.TestDataSets;
using Xunit;

namespace UnitTests.StorageTests.Relational
{
    /// <summary>
    /// 储存试验,其断言应保留在任何后端。
    /// </summary>
    /// <remarks>
    /// 这不属于继承等级制度,不允许任何框架进行更清洁的分离
    /// 测试使用独特的新闻( Guids) , 这样存储就不会
    /// 需要清理,直到所有储存测试完成为止。
    /// </remarks>
    internal class CommonStorageTests
    {
        public CommonStorageTests(IGrainStorage storage) => Storage = storage ?? throw new ArgumentNullException(nameof(storage));

        /// <summary>
        /// 储存提供者正在接受测试。
        /// </summary>
        public IGrainStorage Storage { get; }

        /// <summary>
        /// 创建新的 grain 和 grain 参考对。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="version">最初的州立版本</param>
        /// <returns>A grain 参考和一对州对。</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0022")]
        internal (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(long grainId, string? version)
        {
            var id = GrainId.Create(GrainType.Create("my-grain-type"), GrainIdKeyExtensions.CreateIntegerKey(grainId));
            var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
            return (id, grainState);
        }

        /// <summary>
        /// 创建新的 grain 和 grain 参考对。
        /// </summary>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="version">最初的州立版本</param>
        /// <returns>A grain 参考和一对州对。</returns>
        internal (GrainId GrainId, GrainState<TestState1> GrainState) GetTestReferenceAndState(string grainId, string? version)
        {
            var id = GrainId.Create("my-grain-type", grainId);
            var grainState = new GrainState<TestState1> { State = new TestState1(), ETag = version };
            return (id, grainState);
        }

        /// <summary>
        /// 将提出一个例外。
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
        /// 写入存储的文字, 并试图重写同一状态, NULL 以 ETag 表示为 NULL , 仿佛
        /// grain 刚刚创建。
        /// </summary>
        /// <returns>
        /// 下 <see cref="InconsistentStateException"/> 供应商提出由提供者提出。
        /// 由具体储存的物品检查。
        /// </returns>
        internal async Task<InconsistentStateException> PersistenceStorage_WriteDuplicateFailsWithInconsistentStateException()
        {
            //A grain 数据库将设置随机身份标识。 然后其状态将被设定为无效以模拟事实
            //就像是在成功写作后第二次激活。
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
        /// 将提出一个例外。
        /// </summary>
        /// <returns>
        /// 下 <see cref="InconsistentStateException"/> 供应商提出由提供者提出。
        /// 由具体储存的物品检查。
        /// </returns>
        internal async Task<InconsistentStateException> PersistenceStorage_WriteInconsistentFailsWithInconsistentStateException()
        {
            //某些版本预计不会存放在这类类型和ID的存储中。
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
            //数据是写成的,读取版本号(ETags)后会检查是否正确(它们会改变)。
            //此外,存储WriteRead测试也进行了验证。
            var grainTypeName = GrainTypeGenerator.GetGrainType<Guid>();
            int StartOfRange = 33900;
            int CountOfRange = countOfGrains;

            //由于版本为 NULLL, 存储提供者试图插入此数据
            //以新状态为新状态。如果已经存在此类的数据,写失败
            //和存储提供者扔出。它基本上意味着这个范围
            //选择不当或测试因另一个问题失败。
            var grainStates = Enumerable.Range(StartOfRange, CountOfRange).Select(i => GetTestReferenceAndState($"{prefix}-{Guid.NewGuid():N}-{i}", null)).ToList();

            // 避免第一稿的平行化,以免使制度陷入僵局
            // 国家登记册
            foreach (var grainData in grainStates)
            {
                //一种精神检查器, 其第一版本的状态是无效的。 然后它被存储
                //输入数据库并获得新版本。
                var firstVersion = grainData.GrainState.ETag;
                Assert.Null(firstVersion);

                await Store_WriteRead(grainTypeName, grainData.GrainId, grainData.GrainState).ConfigureAwait(false);
                var secondVersion = grainData.GrainState.ETag;
                Assert.NotEqual(firstVersion, secondVersion);
            }
            ;

            int MaxNumberOfThreads = Environment.ProcessorCount * 3;
            // Parallel.ForEachAsync 的目的是确保储存提供商从
            // 多个线条同时并举,运行系统也会发生。但还是会这样。
            // 限制平行化的程度(当前线条)以避免不必要的
            // 长得更短,如果有几条线结在一起,
            // 通过任务平行执行,可能迫使出现大多数货币一致的情况。

            await Parallel.ForEachAsync(grainStates, new ParallelOptions { MaxDegreeOfParallelism = MaxNumberOfThreads }, async (grainData, ct) =>
            {
                // 此循环将状态连续时间写入数据库, 以确保数据库
                // 版本经过适当更新。
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
        /// 写到存储,清空和读回 并声称版本和州。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="grainTypeName">类型 grain.</param>
        /// <param name="grainReference">下 grain 将指定 Orleans.</param>
        /// <param name="grainState">下 grain 状态 grain 将保持 Orleans 过。</param>
        /// <returns></returns>
        internal async Task Store_WriteClearRead<T>(string grainTypeName, GrainId grainId, GrainState<T> grainState) where T : new()
        {
            //清理的法律状况必须事先以书面形式向储存处书面说明国家
            //清除。写作和清理两者都改变ETag, 因此它们应该有所不同 。
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
        /// 写到存储,读回 并声称版本和州。
        /// </summary>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <param name="grainTypeName">类型 grain.</param>
        /// <param name="grainReference">下 grain 将指定 Orleans.</param>
        /// <param name="grainState">下 grain 状态 grain 将保持 Orleans 过。</param>
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
