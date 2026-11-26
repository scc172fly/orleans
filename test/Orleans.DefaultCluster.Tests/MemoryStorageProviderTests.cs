using Orleans.Storage;
using Orleans.Storage.Internal;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.StorageTests
{
    /// <summary>
    /// 存储存储提供者的 Orleans 测试。
    /// 内存存储供应商存储存储器 grain 状态(非持久性)
    /// 通常用于开发、测试和假设情景,
    /// 不需要在重新启动时连续保持状态。
    /// </summary>
    [TestCategory("Storage"), TestCategory("MemoryStore")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class MemoryStorageProviderTests : HostedTestClusterEnsureDefaultStarted
    {
        public MemoryStorageProviderTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试 grains 能够恢复其存储时的初始状态。
        /// 证明基本合同 grain 状态在激活期间已装船
        /// 存储提供者正确初始化了状态物体。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageProvider_RestoreStateTest()
        {
            var grainWithState = this.GrainFactory.GetGrain<IInitialStateGrain>(0);
            Assert.NotNull(await grainWithState.GetNames());
        }

        /// <summary>
        /// 存储提供者对无效国家价值的测试处理。
        /// 证明 grains 可以储存和检索无效状态,无错误,
        /// 并且可以从无效和非无效的国家价值过渡。
        /// 这对于任择状态情景很重要。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageProvider_NullState()
        {
            var grainWithState = this.GrainFactory.GetGrain<INullStateGrain>(0);
            Assert.NotNull(await grainWithState.GetState());

            await grainWithState.SetStateAndDeactivate(null!);
            await grainWithState.SetStateAndDeactivate(new NullableState { Name = "Thrall" });
        }

        /// <summary>
        /// grain 状态的基本写作和读取操作测试。
        /// 证明在记忆中适当坚持了国家的变化
        /// 并可以检索,以证明基本储存
        /// 国家管理供应商合同。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageProvider_WriteReadStateTest()
        {
            var grainWithState = this.GrainFactory.GetGrain<IInitialStateGrain>(0);

            List<string> names = await grainWithState.GetNames();
            Assert.NotNull(names);
            Assert.Empty(names);

            // 首写
            await grainWithState.AddName("Bob");
            names = await grainWithState.GetNames();
            Assert.NotNull(names);
            Assert.Single(names);
            Assert.Equal("Bob", names[0]);

            // 缺水书写
            await grainWithState.AddName("Alice");
            names = await grainWithState.GetNames();
            Assert.NotNull(names);
            Assert.Equal(2, names.Count);
            Assert.Equal("Bob", names[0]);
            Assert.Equal("Alice", names[1]);
        }

        /// <summary>
        /// 测试存储器供应商对Etag(实体标记)的强制执行情况。
        /// ETags通过确保更新仅成功提供乐观的货币同值控制
        /// 如果国家从读取后没有变化的话。
        /// - Null ETags 工作为首写
        /// - 不合称的ETags造成适当的例外
        /// - 校对ETags允许更新
        /// - 删除状态要求新写作需要无 Etags 。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageGrainEnforcesEtagsTest()
        {
            var memoryStorageGrain = this.GrainFactory.GetGrain<IMemoryStorageGrain>(Random.Shared.Next());

            // 删除删除 grain 状态从空 grain应该是安全的
            await memoryStorageGrain.DeleteStateAsync<object>(
                "grainStoreKey",
                "eTag",
                TestContext.Current.CancellationToken);

            // 读 grain 状态从空 grain,应该是安全的,但什么也不返回。
            var grainState = await memoryStorageGrain.ReadStateAsync<object>(
                "grainStoreKey",
                TestContext.Current.CancellationToken);
            Assert.Null(grainState);

            // 以 etag 写入状态写, 当没有存储时。 大多数存储会失败, 但内存会成功 。
            await memoryStorageGrain.WriteStateAsync(
                "grainId",
                TestGrainState.CreateRandom(),
                TestContext.Current.CancellationToken);

            // 以 Null etag 写入新状态
            string newEtag = await memoryStorageGrain.WriteStateAsync(
                "id",
                TestGrainState.CreateWithEtag(null),
                TestContext.Current.CancellationToken);
            Assert.NotNull(newEtag);

            // 尝试用 Null etag 写入新状态;
            var ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.WriteStateAsync(
                    "id",
                    TestGrainState.CreateWithEtag(null),
                    TestContext.Current.CancellationToken));

            // 尝试用不同的 etag 写入新状态;
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.WriteStateAsync(
                    "id",
                    TestGrainState.CreateWithEtag(newEtag + "a"),
                    TestContext.Current.CancellationToken));

            // 以良好的标签写入新状态;
            string latestEtag = await memoryStorageGrain.WriteStateAsync(
                "id",
                TestGrainState.CreateWithEtag(newEtag),
                TestContext.Current.CancellationToken);
            Assert.NotNull(latestEtag);

            // 尝试用 Null etag 来删除状态
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.DeleteStateAsync<object>(
                    "id",
                    null!,
                    TestContext.Current.CancellationToken));

            // 尝试以错误的标签方式删除状态
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.DeleteStateAsync<object>(
                    "id",
                    latestEtag + "a",
                    TestContext.Current.CancellationToken));

            // 删除状态
            await memoryStorageGrain.DeleteStateAsync<object>(
                "id",
                latestEtag,
                TestContext.Current.CancellationToken);

            // 读 grain 删除已删除 grain 状态,应该是安全的,但什么都不返回。
            grainState = await memoryStorageGrain.ReadStateAsync<object>(
                "id",
                TestContext.Current.CancellationToken);
            Assert.Null(grainState);

            // 尝试删除已删除 grain 状态
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.DeleteStateAsync<object>(
                    "id",
                    latestEtag,
                    TestContext.Current.CancellationToken));

            // 尝试写入状态以删除状态 。
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.WriteStateAsync(
                    "id",
                    TestGrainState.CreateWithEtag(latestEtag),
                    TestContext.Current.CancellationToken));

            // 确保我们能将新状态写入删除状态
            await memoryStorageGrain.WriteStateAsync(
                "id",
                TestGrainState.CreateWithEtag(null),
                TestContext.Current.CancellationToken);
        }

        [Serializable]
        [GenerateSerializer]
        public class TestGrainState : IGrainState<object>
        {
            public static IGrainState<object> CreateRandom()
            {
                return new TestGrainState
                {
                    State = Random.Shared.Next(),
                    ETag = Random.Shared.Next().ToString()
                };
            }

            public static IGrainState<object> CreateWithEtag(string? eTag)
            {
                return new TestGrainState
                {
                    State = Random.Shared.Next(),
                    ETag = eTag
                };
            }

            [Id(0)]
            public object? State { get; set; }

            [Id(1)]
            public string? ETag { get; set; }

            [Id(2)]
            public bool RecordExists { get; set; }
        }
    }
}
