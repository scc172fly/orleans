using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.Runtime;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests
{
    /// <summary>
    /// Orleans 超时机制测试和请求取消。
    /// 
    /// Orleans 执行超时,以防止无限期等待 grain 电话:
    /// - 每一次 grain 呼叫都有可配置的响应超时
    /// - 如果 grain 方法在超时内不完全,则会丢弃 TimeoutException
    /// - 继续执行 silo (未取消)的原始请求
    /// - 其后打给忙碌 grain 的电话可以丢弃,以防止队列集结
    /// 
    /// 这些测试核实:
    /// - 在适当时机提出超时例外
    /// - 请求跟踪在超时后进行适当清理
    /// - 超载呼叫行为下降 grains
    /// 
    /// 注:这些试验改变全球超时设置,因此应单独运行。
    /// </summary>
    [TestArea("Runtime")]
    public class TimeoutTests : HostedTestClusterEnsureDefaultStarted, IDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly TimeSpan originalTimeout;
        private readonly IRuntimeClient runtimeClient;
        private readonly GrainInterfaceTypeResolver typeResolver;

        public TimeoutTests(ITestOutputHelper output, DefaultClusterFixture fixture) : base(fixture)
        {
            this.output = output;
            this.runtimeClient = this.HostedCluster.ServiceProvider.GetRequiredService<IRuntimeClient>();
            // 保存原超时, 在测试后恢复它
            originalTimeout = this.runtimeClient.GetResponseTimeout();
            this.typeResolver = this.HostedCluster.ServiceProvider.GetRequiredService<GrainInterfaceTypeResolver>();
        }

        public virtual void Dispose()
        {
            // 恢复原原超时,以避免影响其他测试
            this.runtimeClient.SetResponseTimeout(originalTimeout);
        }

        /// <summary>
        /// 测试在方法需要时间超过响应超时时时正确调用 grain 超时 。
        /// 核证:
        /// - TimeoutException 被丢弃, 是在配置的超时期后
        /// - 超时发生在预期范围内(不早,也不晚)
        /// - 清理请求追踪(无待处理的请求)
        /// - 立即重新等待同一任务失败
        /// </summary>
        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional"), TestCategory("Timeout")]
        public async Task Timeout_LongMethod()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            bool finished = false;
            var grainName = typeof(ErrorGrain).FullName;
            IErrorGrain grain = this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainName);
            var errorGrainType = this.typeResolver.GetGrainInterfaceType(typeof(IErrorGrain));
            // 设置本次测试的一秒超时
            TimeSpan timeout = TimeSpan.FromMilliseconds(1000);
            this.runtimeClient.SetResponseTimeout(timeout);

            // 调用比超时长4x时间的方法
            Task promise = grain.LongMethod((int)timeout.Multiply(4).TotalMilliseconds);
            //承诺= grain.LongMethodWithError(2000);

            // 注:调试器中可能出现种族状况,因此电话可能完成
            // 测量我们等待超时等多久
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                await promise.WaitAsync(timeout.Multiply(3), cancellationToken);
                finished = true;
                Assert.Fail("Should have thrown");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                stopwatch.Stop();
                Exception baseExc = exc.GetBaseException();
                if (!(baseExc is TimeoutException))
                {
                    Assert.Fail("Should not have got here " + exc);
                }
            }
            output.WriteLine("Waited for " + stopwatch.Elapsed);
            Assert.True(!finished);
            // 核查超时发生在预期范围内(占配置超时的90%至350%)
            Assert.True(stopwatch.Elapsed >= timeout.Multiply(0.9), "Waited less than " + timeout.Multiply(0.9) + ". Waited " + stopwatch.Elapsed);
            Assert.True(stopwatch.Elapsed <= timeout.Multiply(3.5), "Waited longer than " + timeout.Multiply(3.5) + ". Waited " + stopwatch.Elapsed);
            Assert.True(promise.Status == TaskStatus.Faulted);

            // 清理核查请求追踪,清理核查请求跟踪 -- -- 任何请求不应待处理
            Assert.Equal(expected: 0, actual: this.runtimeClient.GetRunningRequestsCount(errorGrainType));

            // 等待超时任务应立刻失败
            try
            {
                stopwatch = new Stopwatch();
                await promise;
                Assert.Fail("Should have thrown");
            }
            catch (Exception exc)
            {
                stopwatch.Stop();
                Exception baseExc = exc.GetBaseException();
                if (!(baseExc is TimeoutException))
                {
                    Assert.Fail("Should not have got here " + exc);
                }
            }
            Assert.True(stopwatch.Elapsed <= timeout.Multiply(0.1), "Waited longer than " + timeout.Multiply(0.1) + ". Waited " + stopwatch.Elapsed);
            Assert.True(promise.Status == TaskStatus.Faulted);
        }

        /// <summary>
        /// 测试称,在 grain 超载时会发生下降行为。
        /// 当一个 grain 忙于处理长期请求和客户点算时,
        /// 其后拨打同一激活的电话可以丢弃,以防止队列集结。
        /// 
        /// 假设情景:
        /// 1. 第一次调用所需时间超过超时 -- -- 客户得到 TimeoutException
        /// 2. 第一次接通时第二通呼叫到达,而第一次接来时仍在运行----应放弃
        /// 3. 只核查在 grain上实际执行的第一通电话
        /// 
        /// 由于第3995号议题,目前跳过。
        /// </summary>
        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact(Skip = "https://github.com/dotnet/orleans/issues/3995"), TestCategory("SlowBVT")]
        public async Task CallThatShouldHaveBeenDroppedNotExecutedTest()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var responseTimeout = TimeSpan.FromSeconds(2);
            this.runtimeClient.SetResponseTimeout(responseTimeout);

            var target = Client.GetGrain<ILongRunningTaskGrain<int>>(Guid.NewGuid());

            // 第一次通话:需要5秒,但客户在2秒钟后会结束
            var delay = TimeSpan.FromSeconds(5);
            var firstCall = target.LongRunningTask(1, responseTimeout + delay);
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            // 第二通:应该丢弃,因为 grain 仍然忙着第一通电话
            var secondCall = target.LongRunningTask(2, TimeSpan.Zero);

            try
            {
                await Assert.ThrowsAsync<TimeoutException>(() => firstCall);
                await Assert.ThrowsAsync<TimeoutException>(() => secondCall);
            }
            catch
            {
                output.WriteLine($"firstCall: {firstCall.Status}, Exception: {firstCall.Exception}");
                output.WriteLine($"secondCall: {secondCall.Status}, Exception: {secondCall.Exception}");
                throw;
            }

            // 等待第一个呼叫完成 silo
            await Task.Delay(delay, cancellationToken);

            // 只核查第一个已执行的呼叫( 值= 1) , 第二个被丢弃
            Assert.Equal(1, await target.GetLastValue());
        }
    }
}
