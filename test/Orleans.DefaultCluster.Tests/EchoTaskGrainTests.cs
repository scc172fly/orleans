using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orleans.Internal;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 基本测试 grain 使用回声 grains.
    /// Echo grains 是返回或处理其收到的输入的简单测试 grains 。
    /// 这些测试核实了基本 Orleans 特征:
    /// - 基本请求/答复通信
    /// - 错误从 grains 传播到客户端
    /// - 超时处理和恢复
    /// -silo 交叉通信
    /// - grain 方法中的阿斯辛/等待模式
    /// - 跨越 grain 边界的可调用型号处理
    /// </summary>
    [TestArea("Runtime")]
    public class EchoTaskGrainTests : HostedTestClusterEnsureDefaultStarted
    {
        private readonly TimeSpan timeout = Debugger.IsAttached ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(10);
        private const string expectedEcho = "Hello from EchoGrain";
        private const string expectedEchoError = "Error from EchoGrain";

        public static readonly TimeSpan Epsilon = TimeSpan.FromSeconds(1);

        public EchoTaskGrainTests(DefaultClusterFixture fixture)
            : base(fixture)
        {
            if (HostedCluster.SecondarySilos.Count == 0)
            {
                HostedCluster.StartAdditionalSilo();
                HostedCluster.WaitForLivenessToStabilizeAsync().Wait();
            }
        }

        /// <summary>
        /// 测试基本 grain 参考创建。
        /// 证明可不经激活获取 grain 参考文献。
        /// 获取 grain 引用是一个与集不连接的本地操作 。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public void EchoGrain_GetGrain()
        {
            _ = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
        }

        /// <summary>
        /// 测试基本 grain 方法,使用字符串回声。
        /// 证明:
        /// - Grain 第一次呼叫即启动
        /// - 方法参数被正确排到 grain
        /// - 返回值正确发送回信给客户
        /// 这是对 grain 通信的最根本检验。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_Echo()
        {
            Stopwatch clock = new Stopwatch();

            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("CreateGrain took {Elapsed}", clock.Elapsed);

            clock.Restart();
            string received = await grain.EchoAsync(expectedEcho);
            this.Logger.LogInformation("EchoGrain.Echo took {Elapsed}", clock.Elapsed);

            Assert.Equal(expectedEcho, received);
        }

        /// <summary>
        /// 测试例外从 grain 传播到客户端。
        /// 证明所实行的例外情形 grain 方法为:
        /// - 进行序列处理,并发回客户
        /// - 以适当的例外类型包装
        /// - 包含原始错误消息
        /// 这确保在分布式假设情景中正确处理错误。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_EchoError()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());

            Task<string> promise = grain.EchoErrorAsync(expectedEchoError);
            await promise.ContinueWith(t =>
            {
                if (!t.IsFaulted) Assert.True(false); // EchoError 不应该成功完成

                Exception exc = t.Exception!;
                while (exc is AggregateException) exc = exc.InnerException!;
                string received = exc.Message;
                Assert.Equal(expectedEchoError, received);
            }, cancellationToken).WaitAsync(timeout, cancellationToken);
        }

        /// <summary>
        /// 使用 ContinueWith 模式测试超时处理。
        /// 证明:
        /// - 长程 grain 方法,根据配置超时
        /// - TimeoutException 扔得当
        /// - 在预期时限内发生超时
        /// 测试 Orleans防止挂号电话无限期中断的能力。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Echo"), TestCategory("Timeout")]
        public async Task EchoGrain_Timeout_ContinueWith()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());

            TimeSpan delay5 = TimeSpan.FromSeconds(30); // grain 呼叫超时(在配置中设置)
            TimeSpan delay45 = TimeSpan.FromSeconds(45);
            TimeSpan delay60 = TimeSpan.FromSeconds(60);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Task<int> promise = grain.BlockingCallTimeoutNoResponseTimeoutOverrideAsync(delay60);
            await promise.ContinueWith(
                t =>
                {
                    if (!t.IsFaulted) Assert.Fail("BlockingCallTimeout should not have completed successfully");

                    Exception exc = t.Exception!;
                    while (exc is AggregateException) exc = exc.InnerException!;
                    Assert.IsAssignableFrom<TimeoutException>(exc);
                }, cancellationToken).WaitAsync(delay45, cancellationToken);
            sw.Stop();
            Assert.True(TimeIsLonger(sw.Elapsed, delay5), $"Elapsed time out of range: {sw.Elapsed}");
            Assert.True(TimeIsShorter(sw.Elapsed, delay60), $"Elapsed time out of range: {sw.Elapsed}");
        }

        /// <summary>
        /// 使用 async/ wait 模式测试超时处理。
        /// 类似超时,但使用现代合成/等待语法。
        /// 验证适当的超时行为, 并使用等待错误处理 。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Echo")]
        public async Task EchoGrain_Timeout_Await()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());

            TimeSpan delay5 = TimeSpan.FromSeconds(5);
            TimeSpan delay25 = TimeSpan.FromSeconds(25);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            try
            {
                int res = await grain.BlockingCallTimeoutAsync(delay25).WaitAsync(cancellationToken);
                Assert.Fail($"BlockingCallTimeout should not have completed successfully, but returned {res}");
            }
            catch (Exception exc)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (exc is AggregateException) exc = exc.InnerException!;
                Assert.IsAssignableFrom<TimeoutException>(exc);
            }
            sw.Stop();
            Assert.True(TimeIsLonger(sw.Elapsed, delay5), $"Elapsed time out of range: {sw.Elapsed}");
            Assert.True(TimeIsShorter(sw.Elapsed, delay25), $"Elapsed time out of range: {sw.Elapsed}");
        }

        /// <summary>
        /// 使用 Task.Result (阻塞等候) 时测试超时处理 。
        /// 证明超时工作是正确的,即使使用同步等待模式。
        /// 注:这种模式一般不鼓励,但需要努力做到兼容。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Echo"), TestCategory("Timeout")]
        public async Task EchoGrain_Timeout_Result()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());

            TimeSpan delay5 = TimeSpan.FromSeconds(5);
            TimeSpan delay25 = TimeSpan.FromSeconds(25);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            try
            {
                // 请注意,这种方法有意使用 Task.Result。
                int res = await Task.Run(() =>
                {
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
                    return grain.BlockingCallTimeoutAsync(delay25).Result;
#pragma warning restore xUnit1031 // Do not use blocking task operations in test method
                }, cancellationToken).WaitAsync(cancellationToken);

                Assert.Fail($"BlockingCallTimeout should not have completed successfully, but returned {res}");
            }
            catch (Exception exc)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (exc is AggregateException) exc = exc.InnerException!;
                Assert.IsAssignableFrom<TimeoutException>(exc);
            }
            sw.Stop();
            Assert.True(TimeIsLonger(sw.Elapsed, delay5), $"Elapsed time out of range: {sw.Elapsed}");
            Assert.True(TimeIsShorter(sw.Elapsed, delay25), $"Elapsed time out of range: {sw.Elapsed}");
        }

        /// <summary>
        /// 测试 grain 多次调用时的状态持久性。
        /// 证明:
        /// - Grains 能够保持通话之间的状态
        /// - 正确更新成功和错误案例的状态
        /// - 最后一个值(或错误消息)是可检索的
        /// 这表明基本有条不紊 grain 行为。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_LastEcho()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Stopwatch clock = new Stopwatch();

            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("CreateGrain took {Elapsed}", clock.Elapsed);

            clock.Restart();
            string? received = await grain.EchoAsync(expectedEcho);
            this.Logger.LogInformation("EchoGrain.Echo took {Elapsed}", clock.Elapsed);

            Assert.Equal(expectedEcho, received);

            clock.Start();

            received = await grain.GetLastEchoAsync();
            this.Logger.LogInformation("EchoGrain.LastEcho took {Elapsed}", clock.Elapsed);

            Assert.Equal(expectedEcho, received); // LastEcho-Echo

            Task<string> promise = grain.EchoErrorAsync(expectedEchoError);
            await promise.ContinueWith(t =>
            {
                if (!t.IsFaulted) Assert.True(false); // EchoError 不应该成功完成

                Exception exc = t.Exception!;
                while (exc is AggregateException) exc = exc.InnerException!;
                string received = exc.Message;
                Assert.Equal(expectedEchoError, received);
            }, cancellationToken).WaitAsync(timeout, cancellationToken);

            clock.Restart();
            received = await grain.GetLastEchoAsync();
            this.Logger.LogInformation("EchoGrain.LastEcho-Error took {Elapsed}", clock.Elapsed);

            Assert.Equal(expectedEchoError, received); // LastEcho-Error
        }

        /// <summary>
        /// 基本测试 grain 活性检查(平方英寸)。
        /// 证明 grains 可以对简单的无参数调用作出反应。
        /// 平方公尺操作有助于健康检查和保持 grains 的启动。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_Ping()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Stopwatch clock = new Stopwatch();

            string what = "CreateGrain";
            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);

            what = "EchoGrain.Ping";
            clock.Restart();

            await grain.PingAsync().WaitAsync(timeout, cancellationToken);
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);
        }

        /// <summary>
        /// 测试 grain的本地调试能力 silo。
        /// 验证内部...silo 通信时的通信 grain 与主机主机通信 silo.
        /// 这一模式对 silo-一级健康检查和诊断非常有用。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_PingSilo_Local()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Stopwatch clock = new Stopwatch();

            string what = "CreateGrain";
            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);

            what = "EchoGrain.PingLocalSilo";
            clock.Restart();
            await grain.PingLocalSiloAsync().WaitAsync(timeout, cancellationToken);
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);
        }

        /// <summary>
        /// 测试 grain的按点特定远程 silos的能力。
        /// 核证交叉silo 通信时的通信 grain 目标 silos.
        /// 这显示了 Orleans将信息传送到特定组节点的能力。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_PingSilo_Remote()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Stopwatch clock = new Stopwatch();

            string what = "CreateGrain";
            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);

            SiloAddress silo1 = HostedCluster.Primary!.SiloAddress;
            SiloAddress silo2 = HostedCluster.SecondarySilos[0].SiloAddress;

            what = "EchoGrain.PingRemoteSilo[1]";
            clock.Restart();
            await grain.PingRemoteSiloAsync(silo1).WaitAsync(timeout, cancellationToken);
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);

            what = "EchoGrain.PingRemoteSilo[2]";
            clock.Restart();
            await grain.PingRemoteSiloAsync(silo2).WaitAsync(timeout, cancellationToken);
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);
        }

        /// <summary>
        /// 测试 grain在组群中切换任何其他 silo 的能力。
        /// 证明 grains 能够动态发现并与其他 silos 进行通信。
        /// 这有助于分发健康检查和对群集地形的认识。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_PingSilo_OtherSilo()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Stopwatch clock = new Stopwatch();

            string what = "CreateGrain";
            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);

            what = "EchoGrain.PingOtherSilo";
            clock.Restart();
            await grain.PingOtherSiloAsync().WaitAsync(timeout, cancellationToken);
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);
        }

        /// <summary>
        /// 测试 grain与集群成员互动的能力。
        /// 证明 grains 可以查询并利用成员信息进行沟通
        /// 这显示了 Orleans成员对成员资格的认识。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_PingSilo_OtherSilo_Membership()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Stopwatch clock = new Stopwatch();

            string what = "CreateGrain";
            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);

            what = "EchoGrain.PingOtherSiloMembership";
            clock.Restart();
            await grain.PingClusterMemberAsync().WaitAsync(timeout, cancellationToken);
            this.Logger.LogInformation("{What} took {Elapsed}", what, clock.Elapsed);
        }

        /// <summary>
        /// 在非再入状态 grains中测试各种合成/等待模式。
        /// 证明屏蔽 grains 处理正确:
        /// - 直接使用自动同步方法呼叫
        /// - 链锁的断声调 (grain 呼叫 grain)
        /// - 不同任务返回模式
        /// 未重新输入 grains 一次处理一个消息 。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoTaskGrain_Await()
        {
            IBlockingEchoTaskGrain g = this.GrainFactory.GetGrain<IBlockingEchoTaskGrain>(GetRandomGrainId());

            string received = await g.Echo(expectedEcho);
            Assert.Equal(expectedEcho, received); // 回回回

            received = await g.CallMethodAV_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodAV_Await

            received = await g.CallMethodTask_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodTask_Await
        }

        /// <summary>
        /// 在重新输入时测试各种合成/等待模式 grains。
        /// 与 EchoTaskGrain_Await 相似,但可与可处理的重新entrant grains
        /// 同时发送多个信件。 这将验证 Orleans的再存支持 。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoTaskGrain_Await_Reentrant()
        {
            IReentrantBlockingEchoTaskGrain g = this.GrainFactory.GetGrain<IReentrantBlockingEchoTaskGrain>(GetRandomGrainId());

            string received = await g.Echo(expectedEcho);
            Assert.Equal(expectedEcho, received); // 回回回

            received = await g.CallMethodAV_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodAV_Await

            received = await g.CallMethodTask_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodTask_Await
        }

        /// <summary>
        /// 测试跨越 grain 边界的无效类型处理。
        /// 证明:
        /// - 非null时,对无效值正确进行序列记录
        /// - 妥善处理无效数值,并作为无效值返回
        /// 这确保了 Orleans 正确处理 .NET 可无效值类型。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoGrain_EchoNullable()
        {
            Stopwatch clock = new Stopwatch();

            clock.Start();
            var grain = this.GrainFactory.GetGrain<IEchoGrain>(Guid.NewGuid());
            this.Logger.LogInformation("CreateGrain took {Elapsed}", clock.Elapsed);

            clock.Restart();
            var now = DateTime.Now;
            var received = await grain.EchoNullable(now);
            this.Logger.LogInformation("EchoGrain.EchoNullable took {Elapsed}", clock.Elapsed);

            Assert.Equal(now, received);

            clock.Restart();
            received = await grain.EchoNullable(null);
            this.Logger.LogInformation("EchoGrain.EchoNullable took {Elapsed}", clock.Elapsed);
            Assert.Null(received);
        }

        // 实用方法...

        private bool TimeIsLonger(TimeSpan time, TimeSpan limit)
        {
            this.Logger.LogInformation("Compare TimeIsLonger: Actual={Time} Limit={Limit} Epsilon={Epsilon}", time, limit, Epsilon);
            return time >= (limit - Epsilon);
        }

        private bool TimeIsShorter(TimeSpan time, TimeSpan limit)
        {
            this.Logger.LogInformation("Compare TimeIsShorter: Actual={Time} Limit={Limit} Epsilon={Epsilon}", time, limit, Epsilon);
            return time <= (limit + Epsilon);
        }
    }
}
