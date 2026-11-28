using Orleans.Storage;
using Orleans.Storage.Internal;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.StorageTests
{
    /// <summary>
    /// Orleans 内存存储提供程序的测试。
    /// 内存存储提供程序将 Grain 状态保存在内存中（非持久化），
    /// 通常用于开发、测试，以及
    /// 不要求跨重启持久保存状态的场景。
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
        /// 测试 Grain 能否从存储恢复其初始状态。
        /// 验证基本契约：Grain 状态在激活期间加载，
        /// 且存储提供程序正确初始化状态对象。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageProvider_RestoreStateTest()
        {
            var grainWithState = this.GrainFactory.GetGrain<IInitialStateGrain>(0);
            Assert.NotNull(await grainWithState.GetNames());
        }

        /// <summary>
        /// 测试存储提供程序对 null 状态值的处理。
        /// 验证 Grain 可以无错误地存储及获取 null 状态，
        /// 并能在 null 与非 null 状态值之间转换。
        /// 这对于可选状态场景非常重要。
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
        /// 测试 Grain 状态的基本写入及读取操作。
        /// 验证状态变化正确保存在内存中
        /// 并且可以读取，展示状态管理的基本存储
        /// 提供程序契约。
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageProvider_WriteReadStateTest()
        {
            var grainWithState = this.GrainFactory.GetGrain<IInitialStateGrain>(0);

            List<string> names = await grainWithState.GetNames();
            Assert.NotNull(names);
            Assert.Empty(names);

            // 第一次写入
            await grainWithState.AddName("Bob");
            names = await grainWithState.GetNames();
            Assert.NotNull(names);
            Assert.Single(names);
            Assert.Equal("Bob", names[0]);

            // 第二次写入
            await grainWithState.AddName("Alice");
            names = await grainWithState.GetNames();
            Assert.NotNull(names);
            Assert.Equal(2, names.Count);
            Assert.Equal("Bob", names[0]);
            Assert.Equal("Alice", names[1]);
        }

        /// <summary>
        /// 测试内存存储提供程序中对 ETag（实体标记）的强制校验。
        /// ETag 提供乐观并发控制，确保只有在状态自读取以来没有变化时
        /// 更新才能成功。此测试验证：
        /// - 初次写入时 null ETag 可用
        /// - 不匹配的 ETag 导致适当的异常
        /// - 正确的 ETag 允许更新
        /// - 已删除的状态在重新写入时要求 null ETag
        /// </summary>
        [Fact, TestCategory("BVT")]
        public async Task MemoryStorageGrainEnforcesEtagsTest()
        {
            var memoryStorageGrain = this.GrainFactory.GetGrain<IMemoryStorageGrain>(Random.Shared.Next());

            // 从状态为空的 Grain 删除状态，应该是安全的。
            await memoryStorageGrain.DeleteStateAsync<object>(
                "grainStoreKey",
                "eTag",
                TestContext.Current.CancellationToken);

            // 读取状态为空的 Grain 的状态，应该安全，但不返回任何内容。
            var grainState = await memoryStorageGrain.ReadStateAsync<object>(
                "grainStoreKey",
                TestContext.Current.CancellationToken);
            Assert.Null(grainState);

            // 存储中没有状态时，使用 ETag 写入状态。大多数存储应使此操作失败，但内存存储应成功。
            await memoryStorageGrain.WriteStateAsync(
                "grainId",
                TestGrainState.CreateRandom(),
                TestContext.Current.CancellationToken);

            // 使用 null ETag 写入新状态
            string newEtag = await memoryStorageGrain.WriteStateAsync(
                "id",
                TestGrainState.CreateWithEtag(null),
                TestContext.Current.CancellationToken);
            Assert.NotNull(newEtag);

            // 尝试使用 null ETag 写入新状态；
            var ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.WriteStateAsync(
                    "id",
                    TestGrainState.CreateWithEtag(null),
                    TestContext.Current.CancellationToken));

            // 尝试使用不同的 ETag 写入新状态；
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.WriteStateAsync(
                    "id",
                    TestGrainState.CreateWithEtag(newEtag + "a"),
                    TestContext.Current.CancellationToken));

            // 使用正确的 ETag 写入新状态；
            string latestEtag = await memoryStorageGrain.WriteStateAsync(
                "id",
                TestGrainState.CreateWithEtag(newEtag),
                TestContext.Current.CancellationToken);
            Assert.NotNull(latestEtag);

            // 尝试使用 null ETag 删除状态
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.DeleteStateAsync<object>(
                    "id",
                    null!,
                    TestContext.Current.CancellationToken));

            // 尝试使用错误的 ETag 删除状态
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

            // 读取已删除的 Grain 状态，应该安全，但不返回任何内容。
            grainState = await memoryStorageGrain.ReadStateAsync<object>(
                "id",
                TestContext.Current.CancellationToken);
            Assert.Null(grainState);

            // 尝试删除已经删除的 Grain 状态
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.DeleteStateAsync<object>(
                    "id",
                    latestEtag,
                    TestContext.Current.CancellationToken));

            // 尝试向已删除的状态写入状态。
            ex = await Assert.ThrowsAsync<MemoryStorageEtagMismatchException>(
                () => memoryStorageGrain.WriteStateAsync(
                    "id",
                    TestGrainState.CreateWithEtag(latestEtag),
                    TestContext.Current.CancellationToken));

            // 确保能够向已删除的状态写入新状态
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
