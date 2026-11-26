using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 基本测试 grain 活动和生命周期行为 Orleans.
    /// 这些测试核查核心功能,包括:
    /// - Grain 启动和身份管理
    /// - 整个活性引爆的持久性国家
    /// - 激活时处理错误
    /// - 支持不同关键类型(长、高、乌隆)
    /// - 请求进行背景传播
    /// Orleans 使用激活模型,即 grains 点火启动,闲置时可以停止使用。
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
        /// 测试基本 grain 激活,有长键和状态更新。
        /// 证明:
        /// - Grains 被调用时适当激活
        /// - 在同一激活中,国家的变化持续存在
        /// - 同一身份的多个 grain 引用同一个身份共享相同的激活
        /// 这显示了 Orleans的虚拟行为者模型,即即使未激活也存在逻辑上的 grains 。
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
        /// 使用“ 指南” 键而不是长键来激活 grain 测试。
        /// 证明 grain 系统妥善处理基于《指导原则》的 grain 身份。
        /// 当没有自然数字识别符号时, grain 键通常使用图形用户界面D。
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
        /// 启动 grain 时因无效参数而失败, 测试错误处理时会发生错误 。
        /// 证明 grains 可在激活期间强制实施限制(e.g.,拒绝某些关键值)。
        /// 这证明 Orleans在 grain 变异物被违反时能够快速失灵。
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
                // 不允许 - 2 的键值
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
        /// 当多个同时请求在激活 grain 期间失败时测试错误处理 。
        /// 证明 Orleans 在没有资源泄漏或僵局的情况下妥善处理突发故障。
        /// 所有同时提出的请求都应收到相同的启动失败。
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
                // 不允许 - 2 的键值
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
        /// 测试 grain 激活,并投放 ulong.MaxValue 到长(结果在 - 1)
        /// 以数字 grain 键对边缘案例进行适当处理。
        /// 这样确保 grain 系统正确处理所有长值。
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
        /// 试验 grain 激活,且 ulong.MinValue (0)投放长。
        /// 验证对零的正确处理为 grain 键。
        /// 零是一个有效的 grain 密钥,应该与其他数字标识符一样工作。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivateDeactivate"), TestCategory("GetGrain")]
        public async Task BasicActivation_ULong_MinValue()
        {
            ulong key1AsUlong = ulong.MinValue; // =零 =
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
        /// 测试 grain 激活, int.MaxValue 作为 grain 键。
        /// 验证大正 grain 键的正确处理。
        /// 这不会确保有大量数字识别特征的溢出或精确问题。
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
        /// 测试 grain 激活, long.MinValue 作为 grain 键。
        /// 验证对最负 grain 密钥的适当处理。
        /// 这样确保 grain 系统处理所有签名长值。
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
        /// 测试 grain 当激活 grains 执行多个接口。
        /// 证明 grains 可通过各种收集类型返回其他 grains 的参考文献。
        /// 这显示了 Orleans对复杂 grain 接口等级的支持。
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
        /// 在重新输入时信件超时后进行恢复测试 grains。
        /// 证明:
        /// - Grains 可以从队列中的过期邮件中恢复
        /// - 事后有效请求在超时后成功
        /// - 系统适当清理过期信息
        /// 测试 Orleans' 抗瞬时故障和超时条件的复原力。
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
                // 设定短响应时间, 并要求做长操作, 在 silo 队列中触发过期 msg 。
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

                // 稍候一小段,以确保 silo 中的过期 msgs 被三排处理。
                Thread.Sleep(TimeSpan.FromSeconds(10));

                // 设定常规响应时间回到时, 希望 msgs ot 成功 。
                this.SetResponseTimeout(prevTimeout);

                this.Logger.LogInformation("About to send a next legit request that should succeed.");
                await grain.DoLongAction(TimeSpan.FromMilliseconds(1), "B_" + 0);
                this.Logger.LogInformation("The request succeeded.");
            }
            finally
            {
                // 设定常规响应时间回到时, 希望 msgs ot 成功 。
                this.SetResponseTimeout(prevTimeout);
            }
        }

        /// <summary>
        /// 测试请求将上下文从客户端传播到 grain。
        /// 证明 Orleans 正确流环境环境环境(如跟踪身份或用户环境)
        /// 从客户到 grain 方法处决。
        /// 请求的背景对于分布式追踪和多租期设想方案至关重要。
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
