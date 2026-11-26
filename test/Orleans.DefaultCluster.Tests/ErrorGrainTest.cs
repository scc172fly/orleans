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
    /// Orleans中的测试错误处理和例外传播。
    /// 这些测试核查 Orleans的强力错误处理能力,包括:
    /// - 例外系列化和跨过程边界传播
    /// - 处理同步和非同步错误
    /// - 超时行为和恢复
    /// - 多次同时故障运行的应激反应测试
    /// - 错误条件下的各种 grain 通信模式
    /// Orleans 确保 grains 中的错误不会崩溃系统,并适当通知打电话者。
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
        /// 测试错误测试的 grain 参考创建 grains。
        /// 证明可以获取错误 grain 参考,并可使用基本方法。
        /// 以此确定错误处理测试的基准。
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
        /// 本地抛出例外( 不在 Orleans grains中) 的测试错误处理 。
        /// 证明标准 .NET 例外处理工作符合预期的比较要求
        /// 在 Orleans中存在分布式错误处理。
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
        /// 测试丢弃例外的 grain 方法是否正确不执行返回的任务。
        /// 证明:
        /// - grain 方法的例外导致任务输入错误状态
        /// - 从任务中可以检索到例外
        /// - 多个人等待同一失败的同一个失败任务 总是抛出相同的例外
        /// 这样可以确保在分配电话中传播可靠的错误。
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
        /// 测试长运行 grain 方法的行为,无错误。
        /// 证明:
        /// - 长期行动的任务不会过早完成
        /// - 任务最终顺利完成
        /// 这样做可以确定基准行为,与超时假设方案进行比较。
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
        /// 测试长期 grain 方法的行为,最终会丢弃例外。
        /// 证明:
        /// - 任务在实际错误发生前不会完成
        /// - 方法完善后,例外情形得到适当宣传
        /// - 时间确保错误在预期延迟后发生
        /// 测试 Orleans处理延迟故障。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("BVT"), TestCategory("ErrorHandling")]
        public async Task ErrorHandlingTimedMethodWithError()
        {
            var grainFullName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName);

            Task task = grain.LongMethodWithError(2000);
            // 由于潜在的种族状况,排除了对任务完成的不实断言。
            await Assert.ThrowsAsync<Exception>(async () => await task);
            Assert.True(task.Status == TaskStatus.Faulted);
        }

        /// <summary>
        /// 压力测试 Orleans 有许多同时延迟的 grain 电话
        /// 证明:
        /// - 该系统可处理数百个同时 grain 通话
        /// - 所有呼吁都顺利地发出,没有僵局
        /// - Orleans 适当管理负荷中的资源
        /// 本文测试 Orleans信件处理和排程的可缩放性。
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
        /// 测试 grain 参考文献的过路采集,作为方法参数。
        /// 证明 Orleans 正确序列和取消复杂类型
        /// 包含 grain 引用。这对于 grains
        /// 需要与多个其它 grains协调。
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
        /// 测试 Orleans' 延迟执行能力。
        /// 证明 grains 可以安排延迟作业时间,而且这些
        /// 指定延迟后正确执行操作。
        /// 测试 Orleans的内部计时器和排期机制。
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
        /// 以简单 grains测试同步方法模式。
        /// 证明 grains 正确处理用于设定和获取状态的同步方法。
        /// 这证明 Orleans支持 grain 执行中的现代收割/等待模式。
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
        /// 试验承诺转发给一个 grain 向另一个 grain发出呼叫的地址。
        /// 证明 Orleans 正确处理链 grain 调用结果
        /// 之一之一 grain 由另一个直接返回的呼叫 grain.
        /// 在 grain 交响情景中,这种模式是常见的。
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
        /// 测试基于 GUID grain 的标识分布和散列。
        /// 证明不同的图形界面数据格式产生分配良好的散列代码
        /// 用于 grain 位置。这对于 silos之间的负载平衡很重要。
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
        /// 使用特定 GUID 模式创建 grain 引用的帮助方法。
        /// 用于测试不同 GUID 格式的散列代码分布 。
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
        /// 测试观察者断开的假想。
        /// 当客户观察家与 grains断开联系时,验证行为。
        /// 测试 Orleans' 观察员模式对事件通知实施情况。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("Revisit"), TestCategory("Observers")]
        public void ObserverTest_Disconnect()
        {
            ObserverTest_DisconnectRunner(false);
        }

        /// <summary>
        /// 测试观察家与多用户断开连接。
        /// 当同一个观察者多次被订阅时, 验证行为
        /// 并断开。 这个测试边缘案例在观察家管理中 。
        /// </summary>
        [TestSuite("BVT")]
        [TestProvider("None")]
        [Fact, TestCategory("Revisit"), TestCategory("Observers")]
        public void ObserverTest_Disconnect2()
        {
            ObserverTest_DisconnectRunner(true);
        }

        /// <summary>
        /// 用于测试观察员断开设想情景的助推法。
        /// observeTwice 参数控制是测试单订阅还是双订阅。
        /// 注:这项测试似乎被评论为人工调试设想。
        /// </summary>
        private void ObserverTest_DisconnectRunner(bool observeTwice)
        {
            // 用于在调试器中手动 Rep和验证
            // 等待发送事件, 因为要丢弃客户端需要 60 秒 grain
            //var simple1 = SimpleGrainTests.GetSimpleGrain();
            //简式2= SimpleGrainFactory.Cast(Domain.Current.Create)(类型(ISimpleGrain)。FullName,
            //    新字典<string, object> {{{ "EventDelay,70000}});
            //var result = new ResultHandle();
            //var 回调=新 SimpleGrainObserver((a, b, r) =>
            //{
            //    r.Done = (a == 10);
            //    output.WriteLine("Received observer callback: A={0} B={1} Done={2}", a, b, r.Done);
            //},结果);
            //var observer = SimpleGrainObserverFactory.CreateObjectReference(callback);
            //(如果)observeTwice)
            //{
            //    simple1.Subscribe(观察者) 等等(; );
            //    simple1.SetB(1). wait () ; // 发送信息给观察者, 以便将其保存在缓存中
            //}
            //simple2.Subscribe(观察者) 等等(; );
            //simple2.SetA(10). 等等 ();
            //Thread.Sleep(2000);
            //Client.Uninitialize();
            //var timeout80sec = TimeSpan.FromSeconds(80);
            //Assert.False(result.WaitForFinished(超时80sec), "WaitforFinished 超时="+超时80sec);
            //// 预防 silo 立刻关闭
            //Thread.Sleep(Debugger.IsAttached ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(5));
        }
    }
}
