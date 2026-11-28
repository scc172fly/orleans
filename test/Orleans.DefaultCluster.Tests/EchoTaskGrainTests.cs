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
    /// 使用 Echo Grain 测试基本 Grain 通信模式。
    /// Echo Grain 是简单的测试 Grain，它返回或处理接收到的输入。
    /// 这些测试验证 Orleans 的基础功能：
    /// - 基本请求与响应通信
    /// - 错误从 Grain 传播至客户端
    /// - 超时处理及恢复
    /// - 跨 Silo 通信
    /// - Grain 方法中的 async/await 模式
    /// - 跨 Grain 边界的可空类型处理
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
        /// 测试基本 Grain 引用创建。
        /// 验证无需激活即可获取 Grain 引用。
        /// 获取 Grain 引用是无需与集群通信的本地操作。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public void EchoGrain_GetGrain()
        {
            _ = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());
        }

        /// <summary>
        /// 使用字符串回显测试基本 Grain 方法调用。
        /// 验证以下情况：
        /// - 首次调用时发生 Grain 激活
        /// - 方法参数被正确封送至 Grain
        /// - 返回值被正确封送回客户端
        /// 这是 Grain 通信最基本的测试。
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
        /// 测试异常从 Grain 传播至客户端。
        /// 验证 Grain 方法引发的异常：
        /// - 被序列化并发送回客户端
        /// - 被包装为适当的异常类型
        /// - 包含原始错误消息
        /// 这确保分布式场景中的错误得到正确处理。
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
                if (!t.IsFaulted) Assert.True(false); // EchoError 不应成功完成

                Exception exc = t.Exception!;
                while (exc is AggregateException) exc = exc.InnerException!;
                string received = exc.Message;
                Assert.Equal(expectedEchoError, received);
            }, cancellationToken).WaitAsync(timeout, cancellationToken);
        }

        /// <summary>
        /// 测试使用 ContinueWith 模式处理超时。
        /// 验证以下情况：
        /// - 长时间运行的 Grain 方法根据配置发生超时
        /// - 正确引发 TimeoutException
        /// - 超时发生在预期的时间范围内
        /// 此测试验证 Orleans 防止挂起的调用无限期阻塞的能力。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Echo"), TestCategory("Timeout")]
        public async Task EchoGrain_Timeout_ContinueWith()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grain = this.GrainFactory.GetGrain<IEchoTaskGrain>(Guid.NewGuid());

            TimeSpan delay5 = TimeSpan.FromSeconds(30); // Grain 调用超时（在配置中设置）
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
        /// 测试使用 async/await 模式处理超时。
        /// 与 Timeout_ContinueWith 类似，但使用现代 async/await 语法。
        /// 验证基于 await 的错误处理能够正确应对超时。
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
        /// 测试使用 Task.Result（阻塞等待）时的超时处理。
        /// 验证即使使用同步等待模式，超时仍能正确工作。
        /// 注意：通常不建议采用此模式，但出于兼容性考虑仍需支持。
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
                // 注意，此方法故意使用 Task.Result。
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
        /// 测试跨多次调用的 Grain 状态保留。
        /// 验证以下情况：
        /// - Grain 可以在调用之间维护状态
        /// - 成功及错误两种情况下，状态均得到正确更新
        /// - 可以获取最近一次的值（或错误消息）
        /// 这展示了有状态 Grain 的基本行为。
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
                if (!t.IsFaulted) Assert.True(false); // EchoError 不应成功完成

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
        /// 测试基本 Grain 存活检查（ping）。
        /// 验证 Grain 能响应简单的无参调用。
        /// Ping 操作适用于健康检查及保持 Grain 激活。
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
        /// 测试 Grain 对其本地 Silo 执行 ping 操作的能力。
        /// 验证 Grain 与托管它的 Silo 进行通信的 Silo 内通信行为。
        /// 此模式适用于 Silo 级健康检查和诊断。
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
        /// 测试 Grain 对指定远程 Silo 执行 ping 操作的能力。
        /// 验证 Grain 可以以特定 Silo 为目标的跨 Silo 通信。
        /// 这展示了 Orleans 将消息路由至指定集群节点的能力。
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
        /// 测试 Grain 对集群中任意其他 Silo 执行 ping 操作的能力。
        /// 验证 Grain 可以动态发现其他 Silo 并与之通信。
        /// 这适用于分布式健康检查及集群拓扑感知。
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
        /// 测试 Grain 与集群成员资格交互的能力。
        /// 验证 Grain 可以查询并使用成员资格信息，与
        /// 其他集群成员通信。这展示了 Orleans 的成员资格感知能力。
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
        /// 测试不可重入 Grain 中的多种 async/await 模式。
        /// 验证阻塞式 Grain 能正确处理：
        /// - 直接异步方法调用
        /// - 链式异步调用（Grain 调用 Grain）
        /// - 不同的基于 Task 的返回模式
        /// 不可重入 Grain 一次处理一条消息。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoTaskGrain_Await()
        {
            IBlockingEchoTaskGrain g = this.GrainFactory.GetGrain<IBlockingEchoTaskGrain>(GetRandomGrainId());

            string received = await g.Echo(expectedEcho);
            Assert.Equal(expectedEcho, received); // Echo

            received = await g.CallMethodAV_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodAV_Await

            received = await g.CallMethodTask_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodTask_Await
        }

        /// <summary>
        /// 测试可重入 Grain 中的多种 async/await 模式。
        /// 与 EchoTaskGrain_Await 类似，但采用可重入 Grain，可以
        /// 并发处理多条消息。此测试验证 Orleans 的可重入性支持。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("Echo")]
        public async Task EchoTaskGrain_Await_Reentrant()
        {
            IReentrantBlockingEchoTaskGrain g = this.GrainFactory.GetGrain<IReentrantBlockingEchoTaskGrain>(GetRandomGrainId());

            string received = await g.Echo(expectedEcho);
            Assert.Equal(expectedEcho, received); // Echo

            received = await g.CallMethodAV_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodAV_Await

            received = await g.CallMethodTask_Await(expectedEcho);
            Assert.Equal(expectedEcho, received); // CallMethodTask_Await
        }

        /// <summary>
        /// 测试跨 Grain 边界的可空类型处理。
        /// 验证以下情况：
        /// - 可空值非 null 时得到正确序列化
        /// - null 值得到正确处理并以 null 返回
        /// 这确保 Orleans 能正确处理 .NET 可空值类型。
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

        // ---------- 辅助方法 ----------

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
