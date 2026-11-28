using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 测试 Orleans 中基本的 Grain 激活及生命周期行为。
    /// 这些测试验证以下核心功能：
    /// - Grain 激活及标识管理
    /// - 跨激活实例的状态持久化
    /// - 激活过程中的错误处理
    /// - 支持不同键类型（long、Guid、ulong）
    /// - 请求上下文传播
    /// Orleans 采用按需激活模型，Grain 在需要时激活，并可在空闲时停用。
    /// </summary>
    [TestArea("Runtime")]
    public class BasicActivationTests : HostedTestClusterEnsureDefaultStarted
    {
        public BasicActivationTests(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        private TimeSpan GetResponseTimeout() => this.Client.ServiceProvider.GetRequiredService<OutsideRuntimeClient>().GetResponseTimeout();
        private void SetResponseTimeout(TimeSpan value) => this.Client.ServiceProvider.GetRequiredService<OutsideRuntimeClient>().SetResponseTimeout(value);

        /// <summary>
        /// 测试使用 long 键的基本 Grain 激活及状态更新。
        /// 验证以下情况：
        /// - 访问 Grain 时会正确激活它
        /// - 状态变化在同一激活实例内得以保留
        /// - 指向同一标识的多个 Grain 引用共享同一激活实例
        /// 这展示了 Orleans 的虚拟 Actor 模型：即使未激活，Grain 在逻辑上仍存在。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_ActivateAndUpdate()
        {
            long g1Key = GetRandomGrainId();
            long g2Key = GetRandomGrainId();
            ITestGrain g1 = this.GrainFactory.GetGrain<ITestGrain>(g1Key);
            ITestGrain g2 = this.GrainFactory.GetGrain<ITestGrain>(g2Key);
            Assert.Equal(g1Key, g1.GetPrimaryKeyLong());
            Assert.Equal(g1Key, await g1.GetKey());
            Assert.Equal(g1Key.ToString(), await g1.GetLabel());
            Assert.Equal(g2Key, await g2.GetKey());
            Assert.Equal(g2Key.ToString(), await g2.GetLabel());

            await g1.SetLabel("one");
            Assert.Equal("one", await g1.GetLabel());
            Assert.Equal(g2Key.ToString(), await g2.GetLabel());

            ITestGrain g1a = this.GrainFactory.GetGrain<ITestGrain>(g1Key);
            Assert.Equal("one", await g1a.GetLabel());
        }

        /// <summary>
        /// 测试使用 Guid 键而非 long 键的 Grain 激活。
        /// 验证 Grain 系统能正确处理基于 Guid 的 Grain 标识。
        /// 不存在自然数字标识符时，通常使用 GUID 作为 Grain 键。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_Guid_ActivateAndUpdate()
        {
            Guid guid1 = Guid.NewGuid();
            Guid guid2 = Guid.NewGuid();

            IGuidTestGrain g1 = this.GrainFactory.GetGrain<IGuidTestGrain>(guid1);
            IGuidTestGrain g2 = this.GrainFactory.GetGrain<IGuidTestGrain>(guid2);
            Assert.Equal(guid1, g1.GetPrimaryKey());
            Assert.Equal(guid1, await g1.GetKey());
            Assert.Equal(guid1.ToString(), await g1.GetLabel());
            Assert.Equal(guid2, await g2.GetKey());
            Assert.Equal(guid2.ToString(), await g2.GetLabel());

            await g1.SetLabel("one");
            Assert.Equal("one", await g1.GetLabel());
            Assert.Equal(guid2.ToString(), await g2.GetLabel());

            IGuidTestGrain g1a = this.GrainFactory.GetGrain<IGuidTestGrain>(guid1);
            Assert.Equal("one", await g1a.GetLabel());
        }

        /// <summary>
        /// 测试因无效参数导致 Grain 激活失败时的错误处理。
        /// 验证 Grain 可以在激活期间强制执行约束（例如拒绝某些键值）。
        /// 这展示了 Orleans 在违反 Grain 不变量时快速失败的能力。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("ErrorHandling"), TestCategory("GetGrain")]
        public async Task BasicActivation_Fail()
        {
            bool failed;
            long key = 0;
            try
            {
                // 此处不允许键值为 -2
                ITestGrain fail = this.GrainFactory.GetGrain<ITestGrain>(-2);
                key = await fail.GetKey();
                failed = false;
            }
            catch (ArgumentException)
            {
                failed = true;
            }

            if (!failed) Assert.Fail("Should have failed, but instead returned " + key);
        }

        /// <summary>
        /// 测试 Grain 激活期间多个并发请求失败时的错误处理。
        /// 验证 Orleans 能正确处理突发失败，而不造成资源泄漏或死锁。
        /// 所有并发请求都应收到同一个激活失败结果。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("ErrorHandling"), TestCategory("GetGrain")]
        public async Task BasicActivation_BurstFail()
        {
            bool failed;
            long key = 0;
            var tasks = new List<Task>();
            try
            {
                // 此处不允许键值为 -2
                var fail = this.GrainFactory.GetGrain<ITestGrainLongOnActivateAsync>(-2);
                for (int i = 0; i < 10000; i++)
                {
                    tasks.Add(fail.GetKey());
                }
                failed = false;
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
                failed = true;
                foreach (var t in tasks)
                {
                    Assert.Equal(typeof(ArgumentException), t.Exception!.InnerException!.GetType());
                }
            }

            if (!failed) Assert.Fail("Should have failed, but instead returned " + key);
        }

        /// <summary>
        /// 测试将 ulong.MaxValue 转换为 long（结果为 -1）后用作 Grain 键的激活。
        /// 验证数字 Grain 键边界情况的正确处理。
        /// 这确保 Grain 系统能正确处理 long 值的完整范围。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_ULong_MaxValue()
        {
            ulong key1AsUlong = ulong.MaxValue; // == -1L
            long key1 = (long)key1AsUlong;

            ITestGrain g1 = this.GrainFactory.GetGrain<ITestGrain>(key1);
            Assert.Equal(key1, g1.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, g1.GetPrimaryKeyLong());
            Assert.Equal(key1, await g1.GetKey());
            Assert.Equal(key1.ToString(CultureInfo.InvariantCulture), await g1.GetLabel());

            await g1.SetLabel("MaxValue");
            Assert.Equal("MaxValue", await g1.GetLabel());

            ITestGrain g1a = this.GrainFactory.GetGrain<ITestGrain>((long)key1AsUlong);
            Assert.Equal("MaxValue", await g1a.GetLabel());
            Assert.Equal(key1, g1a.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, await g1a.GetKey());
        }

        /// <summary>
        /// 测试将 ulong.MinValue（0）转换为 long 后用作 Grain 键的激活。
        /// 验证零作为 Grain 键时的正确处理。
        /// 零是有效的 Grain 键，应与其他数字标识符一样正常工作。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_ULong_MinValue()
        {
            ulong key1AsUlong = ulong.MinValue; // == zero
            long key1 = (long)key1AsUlong;

            ITestGrain g1 = this.GrainFactory.GetGrain<ITestGrain>(key1);
            Assert.Equal(key1, g1.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, g1.GetPrimaryKeyLong());
            Assert.Equal(key1, g1.GetPrimaryKeyLong());
            Assert.Equal(key1, await g1.GetKey());
            Assert.Equal(key1.ToString(CultureInfo.InvariantCulture), await g1.GetLabel());

            await g1.SetLabel("MinValue");
            Assert.Equal("MinValue", await g1.GetLabel());

            ITestGrain g1a = this.GrainFactory.GetGrain<ITestGrain>((long)key1AsUlong);
            Assert.Equal("MinValue", await g1a.GetLabel());
            Assert.Equal(key1, g1a.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, await g1a.GetKey());
        }

        /// <summary>
        /// 测试使用 int.MaxValue 作为 Grain 键的激活。
        /// 验证正确处理较大的正数 Grain 键。
        /// 这确保较大的数字标识符不会引发溢出或精度问题。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_Long_MaxValue()
        {
            long key1 = int.MaxValue;
            ulong key1AsUlong = (ulong)key1;

            ITestGrain g1 = this.GrainFactory.GetGrain<ITestGrain>(key1);
            Assert.Equal(key1, g1.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, g1.GetPrimaryKeyLong());
            Assert.Equal(key1, await g1.GetKey());
            Assert.Equal(key1.ToString(CultureInfo.InvariantCulture), await g1.GetLabel());

            await g1.SetLabel("MaxValue");
            Assert.Equal("MaxValue", await g1.GetLabel());

            ITestGrain g1a = this.GrainFactory.GetGrain<ITestGrain>((long)key1AsUlong);
            Assert.Equal("MaxValue", await g1a.GetLabel());
            Assert.Equal(key1, g1a.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, await g1a.GetKey());
        }

        /// <summary>
        /// 测试使用 long.MinValue 作为 Grain 键的激活。
        /// 验证正确处理最小的负数 Grain 键。
        /// 这确保 Grain 系统能处理有符号 long 值的完整范围。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_Long_MinValue()
        {
            long key1 = long.MinValue;
            ulong key1AsUlong = (ulong)key1;

            ITestGrain g1 = this.GrainFactory.GetGrain<ITestGrain>(key1);
            Assert.Equal((long)key1AsUlong, g1.GetPrimaryKeyLong());
            Assert.Equal(key1, g1.GetPrimaryKeyLong());
            Assert.Equal(key1, await g1.GetKey());
            Assert.Equal(key1.ToString(CultureInfo.InvariantCulture), await g1.GetLabel());

            await g1.SetLabel("MinValue");
            Assert.Equal("MinValue", await g1.GetLabel());

            ITestGrain g1a = this.GrainFactory.GetGrain<ITestGrain>((long)key1AsUlong);
            Assert.Equal("MinValue", await g1a.GetLabel());
            Assert.Equal(key1, g1a.GetPrimaryKeyLong());
            Assert.Equal((long)key1AsUlong, await g1a.GetKey());
        }

        /// <summary>
        /// 测试 Grain 实现多个接口时的激活。
        /// 验证 Grain 可以通过不同集合类型返回对其他 Grain 的引用。
        /// 这展示了 Orleans 对复杂 Grain 接口层次结构的支持。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ActivateDeactivate")]
        public async Task BasicActivation_MultipleGrainInterfaces()
        {
            ITestGrain simple = this.GrainFactory.GetGrain<ITestGrain>(GetRandomGrainId());

            await simple.GetMultipleGrainInterfaces_List();
            this.Logger.LogInformation("GetMultipleGrainInterfaces_List() worked");

            await simple.GetMultipleGrainInterfaces_Array();

            this.Logger.LogInformation("GetMultipleGrainInterfaces_Array() worked");
        }

        /// <summary>
        /// 测试可重入 Grain 在消息超时后的恢复。
        /// 验证以下情况：
        /// - Grain 可以从队列中的过期消息恢复
        /// - 超时之后的有效请求能够成功完成
        /// - 系统正确清理过期消息
        /// 此测试验证 Orleans 应对瞬时故障和超时情况的能力。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("ActivateDeactivate"),
         TestCategory("Reentrancy")]
        public async Task BasicActivation_Reentrant_RecoveryAfterExpiredMessage()
        {
            List<Task> promises = new List<Task>();
            TimeSpan prevTimeout = this.GetResponseTimeout();
            try
            {
                // 设置较短的响应时限，并请求执行长时间操作，以使 Silo 队列中的消息过期。
                TimeSpan shortTimeout = TimeSpan.FromMilliseconds(1000);
                this.SetResponseTimeout(shortTimeout);

                ITestGrain grain = this.GrainFactory.GetGrain<ITestGrain>(GetRandomGrainId());
                int num = 10;
                for (long i = 0; i < num; i++)
                {
                    Task task = grain.DoLongAction(
                        TimeSpan.FromMilliseconds(shortTimeout.TotalMilliseconds * 3),
                        "A_" + i);
                    promises.Add(task);
                }
                try
                {
                    await Task.WhenAll(promises);
                }
                catch (Exception)
                {
                    this.Logger.LogInformation("Done with stress iteration.");
                }

                // 稍等片刻，确保触发 Silo 中的消息过期。
                Thread.Sleep(TimeSpan.FromSeconds(10));

                // 恢复正常响应时限，预期消息能够成功处理。
                this.SetResponseTimeout(prevTimeout);

                this.Logger.LogInformation("About to send a next legit request that should succeed.");
                await grain.DoLongAction(TimeSpan.FromMilliseconds(1), "B_" + 0);
                this.Logger.LogInformation("The request succeeded.");
            }
            finally
            {
                // 恢复正常响应时限，预期消息能够成功处理。
                this.SetResponseTimeout(prevTimeout);
            }
        }

        /// <summary>
        /// 测试请求上下文从客户端传播到 Grain。
        /// 验证 Orleans 正确传递环境上下文（例如跟踪 ID 或用户上下文）
        /// 从客户端一直到 Grain 方法的执行。
        /// 请求上下文对于分布式跟踪和多租户场景至关重要。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("RequestContext"), TestCategory("GetGrain")]
        public async Task BasicActivation_TestRequestContext()
        {
            ITestGrain g1 = this.GrainFactory.GetGrain<ITestGrain>(GetRandomGrainId());
            Task<Tuple<string, string>> promise1 = g1.TestRequestContext();
            Tuple<string, string> requestContext = await promise1;
            this.Logger.LogInformation("Request Context is: {RequestContext}", requestContext);
            Assert.NotNull(requestContext.Item2);
            Assert.NotNull(requestContext.Item1);
        }
    }
}
