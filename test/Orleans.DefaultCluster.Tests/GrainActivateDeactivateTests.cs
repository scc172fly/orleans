using Microsoft.Extensions.Logging;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.ActivationsLifeCycleTests
{
    /// <summary>
    /// 测试 Orleans Grain 激活与停用生命周期。
    /// 验证 Grain 能正确执行 OnActivateAsync 和 OnDeactivateAsync 方法，
    /// 处理激活失败、支持停用后的重新激活，并处理
    /// 激活期间停用或存在长时间运行操作时的复杂场景。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class GrainActivateDeactivateTests : HostedTestClusterEnsureDefaultStarted, IDisposable
    {
        private readonly IActivateDeactivateWatcherGrain watcher;

        public GrainActivateDeactivateTests(DefaultClusterFixture fixture) : base(fixture)
        {
            watcher = this.GrainFactory.GetGrain<IActivateDeactivateWatcherGrain>(0);
            watcher.Clear().Wait();
        }

        public virtual void Dispose()
        {
            watcher.Clear().Wait();
        }

        /// <summary>
        /// 测试观察用 Grain 的基本 Grain 引用创建。
        /// 验证测试基础设施的观察用 Grain 能被正确实例化。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task WatcherGrain_GetGrain()
        {
            IActivateDeactivateWatcherGrain grain = this.GrainFactory.GetGrain<IActivateDeactivateWatcherGrain>(1);
            await grain.Clear();
        }

        /// <summary>
        /// 测试基本 Grain 激活生命周期。
        /// 验证首次访问 Grain 时恰好调用一次 OnActivateAsync。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Activate_Simple()
        {
            int id = Random.Shared.Next();
            ISimpleActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ISimpleActivateDeactivateTestGrain>(id);

            string activation = await grain.DoSomething();

            await CheckNumActivateDeactivateCalls(1, 0, activation, "After activation");
        }

        /// <summary>
        /// 测试基本 Grain 停用生命周期。
        /// 验证显式停用 Grain 时会调用 OnDeactivateAsync，
        /// 并且激活与停用钩子均恰好调用一次。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Deactivate_Simple()
        {
            int id = Random.Shared.Next();
            ISimpleActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ISimpleActivateDeactivateTestGrain>(id);

            // 激活。
            string activation = await grain.DoSomething();

            // 停用。
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");
        }

        /// <summary>
        /// 测试 Grain 停用后的重新激活。
        /// 验证访问已停用的 Grain 时会创建新的激活实例，
        /// 并针对新激活实例再次调用 OnActivateAsync。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Reactivate_Simple()
        {
            int id = Random.Shared.Next();
            ISimpleActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ISimpleActivateDeactivateTestGrain>(id);

            // 激活。
            string activation = await grain.DoSomething();
            // 停用。
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");

            // 重新激活
            string activation2 = await grain.DoSomething();

            Assert.NotEqual(activation, activation2); // 重新激活后创建了新的激活实例
            await CheckNumActivateDeactivateCalls(2, 1, new[] { activation, activation2 }, "After reactivation");
        }

        /// <summary>
        /// 测试采用尾调用优化模式的激活。
        /// 验证在激活逻辑中使用尾调用的 Grain 能正确执行 OnActivateAsync。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Activate_TailCall()
        {
            int id = Random.Shared.Next();
            ITailCallActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITailCallActivateDeactivateTestGrain>(id);

            string activation = await grain.DoSomething();

            await CheckNumActivateDeactivateCalls(1, 0, activation, "After activation");
        }

        /// <summary>
        /// 测试采用尾调用优化模式的停用。
        /// 验证使用尾调用模式的 Grain 得到正确的生命周期管理。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Deactivate_TailCall()
        {
            int id = Random.Shared.Next();
            ITailCallActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITailCallActivateDeactivateTestGrain>(id);

            // 激活。
            string activation = await grain.DoSomething();

            // 停用。
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");
        }

        /// <summary>
        /// 测试使用尾调用模式的 Grain 的重新激活。
        /// 验证使用尾调用的 Grain 能正确停用并重新激活。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Reactivate_TailCall()
        {
            int id = Random.Shared.Next();
            ITailCallActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITailCallActivateDeactivateTestGrain>(id);

            // 激活。
            string activation = await grain.DoSomething();
            // 停用。
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");

            // 重新激活
            string activation2 = await grain.DoSomething();

            Assert.NotEqual(activation, activation2); // 重新激活后创建了新的激活实例
            await CheckNumActivateDeactivateCalls(2, 1, new[] { activation, activation2 }, "After reactivation");
        }

        /// <summary>
        /// 测试具有长时间运行操作的 Grain 的停用。
        /// 验证具有可重入的长时间运行任务的 Grain 能正确停用，
        /// 并在后续访问时创建新的激活实例。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("Reentrancy")]
        public async Task LongRunning_Deactivate()
        {
            int id = Random.Shared.Next();
            ILongRunningActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ILongRunningActivateDeactivateTestGrain>(id);

            // 激活。
            string activation = await grain.DoSomething();

            await CheckNumActivateDeactivateCalls(1, 0, activation, "Before deactivation");

            // 停用。
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");

            // 重新激活
            string activation2 = await grain.DoSomething();

            Assert.NotEqual(activation, activation2); // 重新激活后创建了新的激活实例；

            await CheckNumActivateDeactivateCalls(2, 1, new[] { activation, activation2 }, "After reactivation");
        }

        /// <summary>
        /// 测试 Grain 激活期间引发的异常的处理。
        /// 验证 OnActivateAsync 中的异常会正确传播至调用方，
        /// 且 Grain 激活失败。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task BadActivate_Await()
        {
            try
            {
                int id = Random.Shared.Next();
                IBadActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<IBadActivateDeactivateTestGrain>(id);

                await grain.ThrowSomething();

                Assert.Fail("Expected ThrowSomething call to fail as unable to Activate grain");
            }
            catch (ApplicationException exc)
            {
                Assert.Contains("Application-OnActivateAsync", exc.Message);
            }
        }

        /// <summary>
        /// 测试激活引发异常时 Grain 方法是否失败。
        /// 验证 OnActivateAsync 失败时所有 Grain 方法调用均适当失败。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task BadActivate_GetValue()
        {
            try
            {
                int id = Random.Shared.Next();
                IBadActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<IBadActivateDeactivateTestGrain>(id);

                long key = await grain.GetKey();

                Assert.Fail("Expected ThrowSomething call to fail as unable to Activate grain, but returned " + key);
            }
            catch (ApplicationException exc)
            {
                Assert.Contains("Application-OnActivateAsync", exc.Message);
            }
        }

        /// <summary>
        /// 测试激活失败通过 Grain 间调用传播。
        /// 验证 Grain 经由另一个 Grain 的方法调用间接激活时，
        /// 激活异常能正确传播。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task BadActivate_Await_ViaOtherGrain()
        {
            try
            {
                int id = Random.Shared.Next();
                ICreateGrainReferenceTestGrain grain = this.GrainFactory.GetGrain<ICreateGrainReferenceTestGrain>(id);

                await grain.ForwardCall(this.GrainFactory.GetGrain<IBadActivateDeactivateTestGrain>(id));

                Assert.Fail("Expected ThrowSomething call to fail as unable to Activate grain");
            }
            catch (ApplicationException exc)
            {
                Assert.Contains("Application-OnActivateAsync", exc.Message);
            }
        }

        /// <summary>
        /// 测试 Grain 构造函数引发的异常的处理。
        /// 验证构造函数失败能得到正确处理，而不导致
        /// 无效操作异常，从而确保清晰的失败语义。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Constructor_Bad_Await()
        {
            try
            {
                int id = Random.Shared.Next();
                IBadConstructorTestGrain grain = this.GrainFactory.GetGrain<IBadConstructorTestGrain>(id);

                await grain.DoSomething();

                Assert.Fail("Expected ThrowSomething call to fail as unable to Activate grain");
            }
            catch (TimeoutException te)
            {
                Console.WriteLine("Received timeout: " + te);
                throw; // 使测试失败
            }
            catch (Exception exc)
            {
                AssertIsNotInvalidOperationException(exc, "Constructor");
            }
        }

        /// <summary>
        /// 测试 Grain 能否在其构造函数中创建 Grain 引用。
        /// 验证 Grain 构造阶段允许创建 Grain 引用。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Constructor_CreateGrainReference()
        {
            int id = Random.Shared.Next();
            ICreateGrainReferenceTestGrain grain = this.GrainFactory.GetGrain<ICreateGrainReferenceTestGrain>(id);

            string activation = await grain.DoSomething();
            Assert.NotNull(activation);
        }

        /// <summary>
        /// 测试采用基于 Task 的激活模式的 Grain 的停用。
        /// 验证使用 Task Action 激活逻辑的 Grain 得到正确的生命周期管理。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task TaskAction_Deactivate()
        {
            int id = Random.Shared.Next();
            ITaskActionActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITaskActionActivateDeactivateTestGrain>(id);

            // 激活。
            string activation = await grain.DoSomething();

            // 停用。
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation.ToString());
        }

        /// <summary>
        /// 测试 Grain 在激活期间尝试停用的场景。
        /// 验证 Orleans 能正确处理并拒绝这一无效状态转换，
        /// 防止激活生命周期中出现竞态条件。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task DeactivateOnIdleWhileActivate()
        {
            int id = Random.Shared.Next();
            IDeactivatingWhileActivatingTestGrain grain = this.GrainFactory.GetGrain<IDeactivatingWhileActivatingTestGrain>(id);

            try
            {
                string activation = await grain.DoSomething();
                Assert.Fail("Should have thrown.");
            }
            catch (OrleansMessageRejectionException exc)
            {
                this.Logger.LogInformation(exc, "Thrown as expected");
                Assert.True(
                    exc.Message.Contains("Forwarding failed"),
                    "Did not get expected exception message returned: " + exc.Message);
            }
        }

        private async Task CheckNumActivateDeactivateCalls(
            int expectedActivateCalls,
            int expectedDeactivateCalls,
            string forActivation,
            string? when = null)
        {
            await CheckNumActivateDeactivateCalls(
                expectedActivateCalls,
                expectedDeactivateCalls,
                new string[] { forActivation },
                when)
            ;
        }

        private async Task CheckNumActivateDeactivateCalls(
            int expectedActivateCalls,
            int expectedDeactivateCalls,
            string[] forActivations,
            string? when = null)
        {
            string[] activateCalls = await watcher.GetActivateCalls();
            Assert.Equal(expectedActivateCalls, activateCalls.Length);

            string[] deactivateCalls = await watcher.GetDeactivateCalls();
            Assert.Equal(expectedDeactivateCalls, deactivateCalls.Length);

            for (int i = 0; i < expectedActivateCalls; i++)
            {
                Assert.Equal(forActivations[i], activateCalls[i]);
            }

            for (int i = 0; i < expectedDeactivateCalls; i++)
            {
                Assert.Equal(forActivations[i], deactivateCalls[i]);
            }
        }

        private static void AssertIsNotInvalidOperationException(Exception thrownException, string expectedMessageSubstring)
        {
            Console.WriteLine("Received exception: " + thrownException);
            Exception e = thrownException.GetBaseException();
            Console.WriteLine("Nested exception type: " + e.GetType().FullName);
            Console.WriteLine("Nested exception message: " + e.Message);

            Assert.IsAssignableFrom<Exception>(e);
            Assert.False(e is InvalidOperationException);
            Assert.True(e.Message.Contains(expectedMessageSubstring), "Did not get expected exception message returned: " + e.Message);

        }
    }
}
