using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Internal;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 测试 Orleans 中的错误处理和异常传播。
    /// 这些测试验证 Orleans 的错误处理能力，包括：
    /// - 跨进程边界的异常序列化和传播
    /// - 同步及异步错误处理
    /// - 超时行为与恢复
    /// - 大量并发失败操作的压力测试
    /// - 错误情况下的多种 Grain 通信模式
    /// Orleans 确保 Grain 中的错误不会导致系统崩溃，并能正确传递给调用方。
    /// </summary>
    [TestArea("Runtime")]
    public class ErrorGrainTest : HostedTestClusterEnsureDefaultStarted
    {
        private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
        private readonly ITestOutputHelper output;

        public ErrorGrainTest(ITestOutputHelper output, DefaultClusterFixture fixture) : base(fixture)
        {
            this.output = output;
        }

        /// <summary>
        /// 测试用于错误测试的 Grain 的基本引用创建。
        /// 验证能够获取错误测试 Grain 的引用并调用基本方法。
        /// 这为错误处理测试建立基准。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling")]
        public async Task ErrorGrain_GetGrain()
        {
            var grainFullName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);
            _ = await grain.GetA();
        }

        /// <summary>
        /// 测试本地引发的异常（不在 Orleans Grain 中）的错误处理。
        /// 验证标准 .NET 异常处理是否按预期工作，以便与
        /// Orleans 中的分布式错误处理进行对比。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling")]
        public async Task ErrorHandlingLocalError()
        {
            LocalErrorGrain localGrain = new LocalErrorGrain();

            Task<int> intPromise = localGrain.GetAxBError();
            try
            {
                await intPromise;
                Assert.Fail("Should not have executed");
            }
            catch (Exception exc2)
            {
                Assert.Equal(exc2.GetBaseException().Message, (new Exception("GetAxBError-Exception")).Message);
            }

            Assert.True(intPromise.Status == TaskStatus.Faulted);
        }

        /// <summary>
        /// 测试引发异常的 Grain 方法能正确使其返回的 Task 进入失败状态。
        /// 验证以下情况：
        /// - Grain 方法中的异常使 Task 进入 Faulted 状态
        /// - 可从 Task 获取异常
        /// - 多次 await 同一个失败的 Task 会始终引发相同的异常
        /// 这确保分布式调用中的错误能够可靠传播。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling")]
        public async Task ErrorHandlingGrainError1()
        {
            var grainFullName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);

            Task<int> intPromise = grain.GetAxBError();
            try
            {
                await intPromise;
                Assert.Fail("Should have thrown");
            }
            catch (Exception)
            {
                Assert.True(intPromise.Status == TaskStatus.Faulted);
            }

            try
            {
                await intPromise;
                Assert.Fail("Should have thrown");
            }
            catch (Exception exc2)
            {
                Assert.True(intPromise.Status == TaskStatus.Faulted);
                Assert.Equal((new Exception("GetAxBError-Exception")).Message, exc2.GetBaseException().Message);
            }

            Assert.True(intPromise.Status == TaskStatus.Faulted);
        }

        /// <summary>
        /// 测试无错误的长时间运行 Grain 方法的行为。
        /// 验证以下情况：
        /// - 长时间运行操作的任务不会提前完成
        /// - Task 最终成功完成
        /// 这为与超时场景比较建立行为基准。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling")]
        public async Task ErrorHandlingTimedMethod()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grainFullName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);

            Task promise = grain.LongMethodUntilReleased();
            try
            {
                await grain.WaitForLongMethodToStart().WaitAsync(timeout, cancellationToken);
                Assert.False(promise.IsCompleted, "The task shouldn't complete before the grain method is released.");

                await grain.ReleaseLongMethod().WaitAsync(timeout, cancellationToken);
                await promise.WaitAsync(timeout, cancellationToken);

                Assert.Equal(TaskStatus.RanToCompletion, promise.Status);
            }
            finally
            {
                await grain.ReleaseLongMethod().WaitAsync(timeout, cancellationToken);
            }
        }

        /// <summary>
        /// 测试最终引发异常的长时间运行 Grain 方法的行为。
        /// 验证以下情况：
        /// - 任务直到实际发生错误才会完成
        /// - 方法完成时异常得到正确传播
        /// - 时序确保在预期的延迟之后发生错误
        /// 此测试验证 Orleans 处理延迟失败的能力。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling")]
        public async Task ErrorHandlingTimedMethodWithError()
        {
            var grainFullName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);

            Task task = grain.LongMethodWithError(2000);
            // 由于可能出现竞态条件，移除了有关任务完成状态的不稳定断言。
            await Assert.ThrowsAsync<Exception>(async () => await task);
            Assert.True(task.Status == TaskStatus.Faulted);
        }

        /// <summary>
        /// 使用大量并发延迟 Grain 调用对 Orleans 进行压力测试。
        /// 验证以下情况：
        /// - 系统能够处理数百个并发 Grain 调用
        /// - 所有调用均成功完成，不发生死锁
        /// - Orleans 在负载下正确管理资源
        /// 此测试验证 Orleans 消息处理与调度的可扩展性。
        /// </summary>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional"), TestCategory("ErrorHandling"), TestCategory("Stress")]
        public async Task StressHandlingMultipleDelayedRequests()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId());
            bool once = true;
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < 500; i++)
            {
                Task promise = grain.DelayMethod(1);
                tasks.Add(promise);
                if (once)
                {
                    once = false;
                    await promise;
                }

            }
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        /// <summary>
        /// 测试将 Grain 引用集合用作方法参数。
        /// 验证 Orleans 能正确序列化和反序列化
        /// 包含 Grain 引用的复杂类型。这对于 Grain 需要
        /// 与多个其他 Grain 协调的场景十分重要。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling"), TestCategory("GrainReference")]
        public async Task ArgumentTypes_ListOfGrainReferences()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var grainFullName = typeof(ErrorGrain).FullName;
            List<IErrorGrain> list = new List<IErrorGrain>();
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);
            list.Add(this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName));
            list.Add(this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName));
            await grain.AddChildren(list).WaitAsync(timeout, cancellationToken);
        }

        /// <summary>
        /// 测试 Orleans 的延迟执行能力。
        /// 验证 Grain 可以安排延迟操作，而且这些
        /// 操作在指定延迟之后正确执行。
        /// 此测试验证 Orleans 的内部定时器及调度机制。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("AsynchronyPrimitives"), TestCategory("ErrorHandling")]
        public async Task AC_DelayedExecutor_2()
        {
            var grainFullName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);
            Task<bool> promise = grain.ExecuteDelayed(TimeSpan.FromMilliseconds(2000));
            bool result = await promise;
            Assert.True(result);
        }

        /// <summary>
        /// 测试简单 Grain 中的异步方法模式。
        /// 验证 Grain 能正确处理用于设置和获取状态的异步方法。
        /// 这展示了 Orleans 在 Grain 实现中对现代 async/await 模式的支持。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("SimpleGrain")]
        public async Task SimpleGrain_AsyncMethods()
        {
            ISimpleGrainWithAsyncMethods grain = this.GrainFactory.GetGrain<ISimpleGrainWithAsyncMethods>(GetRandomGrainId());
            Task setPromise = grain.SetA_Async(10);
            await setPromise;

            setPromise = grain.SetB_Async(30);
            await setPromise;

            var value = await grain.GetAxB_Async();
            Assert.Equal(300, value);
        }

        /// <summary>
        /// 测试一个 Grain 将调用转发给另一个 Grain 时的 Promise 转发。
        /// 验证 Orleans 正确处理链式 Grain 调用，其中一次 Grain 调用的结果
        /// 直接由另一个 Grain 返回。
        /// 此模式在 Grain 编排场景中很常见。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("SimpleGrain")]
        public async Task SimpleGrain_PromiseForward()
        {
            ISimpleGrain forwardGrain = this.GrainFactory.GetGrain<IPromiseForwardGrain>(GetRandomGrainId());
            Task<int> promise = forwardGrain.GetAxB(5, 6);
            int result = await promise;
            Assert.Equal(30, result);
        }

        /// <summary>
        /// 测试基于 GUID 的 Grain ID 的分布与哈希。
        /// 验证不同 GUID 模式能为 Grain 放置产生分布良好的哈希码，
        /// 这对跨 Silo 的负载均衡非常重要。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("SimpleGrain")]
        public void SimpleGrain_GuidDistribution()
        {
            int n = 0x1111;
            CreateGR(n, 1);
            CreateGR(n + 1, 1);
            CreateGR(n + 2, 1);
            CreateGR(n + 3, 1);
            CreateGR(n + 4, 1);

            Logger.LogInformation("================");

            CreateGR(n, 2);
            CreateGR(n + 1, 2);
            CreateGR(n + 2, 2);
            CreateGR(n + 3, 2);
            CreateGR(n + 4, 2);

            Logger.LogInformation("DONE.");
        }

        /// <summary>
        /// 使用特定 GUID 模式创建 Grain 引用的辅助方法。
        /// 用于测试不同 GUID 格式的哈希码分布。
        /// </summary>
        private void CreateGR(int n, int type)
        {
            Guid guid;
            if (type == 1)
            {
                guid = Guid.Parse(string.Format("00000000-0000-0000-0000-{0:X12}", n));
            }
            else
            {
                guid = Guid.Parse(string.Format("{0:X8}-0000-0000-0000-000000000000", n));
            }
            IEchoGrain grain = this.GrainFactory.GetGrain<IEchoGrain>(guid);
            GrainId grainId = ((GrainReference)grain.AsReference<IEchoGrain>()).GrainId;
            output.WriteLine("Guid = {0}, Guid.HashCode = x{1:X8}, GrainId.HashCode = x{2:X8}, GrainId.UniformHashCode = x{3:X8}", guid, guid.GetHashCode(), grainId.GetHashCode(), grainId.GetUniformHashCode());
        }

        /// <summary>
        /// 测试观察者断开连接的场景。
        /// 验证客户端观察者从 Grain 断开连接时的行为。
        /// 此测试验证 Orleans 用于事件通知的观察者模式实现。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("Revisit"), TestCategory("Observers")]
        public void ObserverTest_Disconnect()
        {
            ObserverTest_DisconnectRunner(false);
        }

        /// <summary>
        /// 测试存在多个订阅时观察者断开连接的场景。
        /// 验证同一个观察者被多次订阅
        /// 后断开连接时的行为。此测试覆盖观察者管理中的边界情况。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("Revisit"), TestCategory("Observers")]
        public void ObserverTest_Disconnect2()
        {
            ObserverTest_DisconnectRunner(true);
        }

        /// <summary>
        /// 用于测试观察者断开连接场景的辅助方法。
        /// observeTwice 参数控制是测试单次订阅还是两次订阅。
        /// 注意：此测试似乎已被注释掉，用于手动调试场景。
        /// </summary>
        private void ObserverTest_DisconnectRunner(bool observeTwice)
        {
            // 用于在调试器中手动复现及验证
            // 等待一段时间再发送事件，因为移除客户端 Grain 需要 60 秒
            //var simple1 = SimpleGrainTests.GetSimpleGrain();
            //var simple2 = SimpleGrainFactory.Cast(Domain.Current.Create(typeof(ISimpleGrain).FullName,
            //    new Dictionary<string, object> { { "EventDelay", 70000 } }));
            //var result = new ResultHandle();
            //var callback = new SimpleGrainObserver((a, b, r) =>
            //{
            //    r.Done = (a == 10);
            //    output.WriteLine("Received observer callback: A={0} B={1} Done={2}", a, b, r.Done);
            //}, result);
            //var observer = SimpleGrainObserverFactory.CreateObjectReference(callback);
            //if (observeTwice)
            //{
            //    simple1.Subscribe(observer).Wait();
            //    simple1.SetB(1).Wait(); // send a message to the observer to get it in the cache
            //}
            //simple2.Subscribe(observer).Wait();
            //simple2.SetA(10).Wait();
            //Thread.Sleep(2000);
            //Client.Uninitialize();
            //var timeout80sec = TimeSpan.FromSeconds(80);
            //Assert.False(result.WaitForFinished(timeout80sec), "WaitforFinished Timeout=" + timeout80sec);
            //// 防止 Silo 立即关闭
            //Thread.Sleep(Debugger.IsAttached ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(5));
        }
    }
}
