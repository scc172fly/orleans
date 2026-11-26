using Microsoft.Extensions.Logging;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.ActivationsLifeCycleTests
{
    /// <summary>
    /// Orleans grain 激活和解除作用生命周期的测试。
    /// 证明 grains 正确执行 OnActivateAsync 和 OnDeactivateAsync 方法,
    /// 处理启动失败,支持在停止后恢复运行,并管理
    /// 复杂情景,如启动期间或长期运行的操作中停止使用。
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
        /// 基本测试 grain 查看器的引用创建 grain.
        /// 验证测试基础设施的监视器 grain 能够适当即时。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task WatcherGrain_GetGrain()
        {
            IActivateDeactivateWatcherGrain grain = this.GrainFactory.GetGrain<IActivateDeactivateWatcherGrain>(1);
            await grain.Clear();
        }

        /// <summary>
        /// 基本 grain 活化生命周期测试。
        /// 验证 OnActivateAsync 被精确调用 grain 首次访问。
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
        /// 测试基本 grain 解除作用生命周期。
        /// 验证 OnDeactivateAsync 调用时, grain 明确解除了作用,
        /// 激活和解除作用钩都被精确地称为一次
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Deactivate_Simple()
        {
            int id = Random.Shared.Next();
            ISimpleActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ISimpleActivateDeactivateTestGrain>(id);

            // 激活
            string activation = await grain.DoSomething();

            // 取消活动
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");
        }

        /// <summary>
        /// 关闭后恢复 grain 测试。
        /// 验证在访问被禁用的 grain时创建了新激活,
        /// 与 OnActivateAsync 再次呼吁新激活。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Reactivate_Simple()
        {
            int id = Random.Shared.Next();
            ISimpleActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ISimpleActivateDeactivateTestGrain>(id);

            // 激活
            string activation = await grain.DoSomething();
            // 取消活动
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");

            // 重新激活
            string activation2 = await grain.DoSomething();

            Assert.NotEqual(activation, activation2); // 重新激活后创建的新激活
            await CheckNumActivateDeactivateCalls(2, 1, new[] { activation, activation2 }, "After reactivation");
        }

        /// <summary>
        /// 测试启动时会使用尾声呼叫优化模式
        /// 验证在启动逻辑中使用尾声的 grains 正确执行 OnActivateAsync。
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
        /// 测试停止使用尾声呼叫优化模式。
        /// 使用尾巴呼叫模式验证 grains 的妥善生命周期管理。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Deactivate_TailCall()
        {
            int id = Random.Shared.Next();
            ITailCallActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITailCallActivateDeactivateTestGrain>(id);

            // 激活
            string activation = await grain.DoSomething();

            // 取消活动
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");
        }

        /// <summary>
        /// 使用尾巴呼叫模式恢复 grains 测试。
        /// 验证尾巴呼叫 grains 可适当停止使用和重新启动。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task Reactivate_TailCall()
        {
            int id = Random.Shared.Next();
            ITailCallActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITailCallActivateDeactivateTestGrain>(id);

            // 激活
            string activation = await grain.DoSomething();
            // 取消活动
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");

            // 重新激活
            string activation2 = await grain.DoSomething();

            Assert.NotEqual(activation, activation2); // 重新激活后创建的新激活
            await CheckNumActivateDeactivateCalls(2, 1, new[] { activation, activation2 }, "After reactivation");
        }

        /// <summary>
        /// 试验 grains 长期运作。
        /// 验证 grains 具有重新输入的长期长期任务,可适当停止使用
        /// 并创建了一个新的激活 以后的接入。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("Reentrancy")]
        public async Task LongRunning_Deactivate()
        {
            int id = Random.Shared.Next();
            ILongRunningActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ILongRunningActivateDeactivateTestGrain>(id);

            // 激活
            string activation = await grain.DoSomething();

            await CheckNumActivateDeactivateCalls(1, 0, activation, "Before deactivation");

            // 取消活动
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation, "After deactivation");

            // 重新激活
            string activation2 = await grain.DoSomething();

            Assert.NotEqual(activation, activation2); // 重新激活后创建的新激活;

            await CheckNumActivateDeactivateCalls(2, 1, new[] { activation, activation2 }, "After reactivation");
        }

        /// <summary>
        /// 启动 grain 期间对例外情形的处理测试。
        /// 验证 OnActivateAsync 中的例外情形是否适当地向调用者传播
        /// 且该 grain 未能启动。
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
        /// 测试激活时丢弃例外时 grain 方法失败 。
        /// 证明所有 grain 方法在 OnActivateAsync 失败时均无法适当调用。
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
        /// 通过 grain-至grain 电话传播测试启动失败。
        /// 验证在 grain通知时是否适当宣传激活例外情形
        /// 是通过另外 grain的方法调用间接激活的。
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
        /// 处理 grain 建筑工件中投掷的例外情形的测试。
        /// 验证建造者故障是否得到妥善处理,而不导致
        /// 无效操作例外, 确保干净故障语义 。
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
                throw; // 故障测试
            }
            catch (Exception exc)
            {
                AssertIsNotInvalidOperationException(exc, "Constructor");
            }
        }

        /// <summary>
        /// 测试 grains 能够在构建器中创建 grain 引用。
        /// 验证 grain 允许在 grain 施工阶段。
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
        /// 试验 grains 使用基于任务的活动模式。
        /// 使用任务行动激活逻辑,验证 grains 的适当的生命周期管理。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task TaskAction_Deactivate()
        {
            int id = Random.Shared.Next();
            ITaskActionActivateDeactivateTestGrain grain = this.GrainFactory.GetGrain<ITaskActionActivateDeactivateTestGrain>(id);

            // 激活
            string activation = await grain.DoSomething();

            // 取消活动
            await HostedCluster.DeactivateAsync(grain);

            await CheckNumActivateDeactivateCalls(1, 1, activation.ToString());
        }

        /// <summary>
        /// 测试 grain 在激活期间试图停止作用的设想情况。
        /// 证明 Orleans 正确处理和拒绝这一无效的国家过渡,
        /// 在激活生命周期中防止种族条件。
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
