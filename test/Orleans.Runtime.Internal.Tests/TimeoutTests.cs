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
    /// 测试 Orleans 超时机制及请求取消。
    /// 
    /// Orleans 实现超时机制，防止无限期等待 Grain 调用：
    /// - 每次 Grain 调用都有可配置的响应超时。
    /// - 如果 Grain 方法未在超时内完成，则抛出 TimeoutException。
    /// - 原始请求仍在 Silo 上继续执行（未被取消）。
    /// - 对忙碌 Grain 的后续调用可能被丢弃，以防止队列积压。
    /// 
    /// 这些测试验证：
    /// - 在适当的时间抛出超时异常。
    /// - 超时后正确清理请求跟踪记录。
    /// - 过载 Grain 的调用丢弃行为。
    /// 
    /// 注意：这些测试会修改全局超时设置，因此应隔离运行。
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
            // 保存原始超时值，以便测试结束后恢复。
            originalTimeout = this.runtimeClient.GetResponseTimeout();
            this.typeResolver = this.HostedCluster.ServiceProvider.GetRequiredService<GrainInterfaceTypeResolver>();
        }

        public virtual void Dispose()
        {
            // 恢复原始超时值，以免影响其他测试。
            this.runtimeClient.SetResponseTimeout(originalTimeout);
        }

        /// <summary>
        /// 测试 Grain 方法耗时超过响应超时时，Grain 调用是否正确超时。
        /// 验证：
        /// - 配置的超时时间过后抛出 TimeoutException。
        /// - 超时发生在预期范围内（不会过早或过晚）。
        /// - 请求跟踪记录得到清理（无残留请求）。
        /// - 再次等待同一个任务时立即失败。
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
            // 为此测试设置 1 秒超时。
            TimeSpan timeout = TimeSpan.FromMilliseconds(1000);
            this.runtimeClient.SetResponseTimeout(timeout);

            // 调用耗时为超时值 4 倍的方法。
            Task promise = grain.LongMethod((int)timeout.Multiply(4).TotalMilliseconds);
            //promise = grain.LongMethodWithError(2000);

            // 注意：调试器中存在潜在竞争条件，调用可能会完成。
            // 测量等待超时所需的时间。
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
            // 验证超时发生在预期范围内（配置超时的 90% 至 350%）。
            Assert.True(stopwatch.Elapsed >= timeout.Multiply(0.9), "Waited less than " + timeout.Multiply(0.9) + ". Waited " + stopwatch.Elapsed);
            Assert.True(stopwatch.Elapsed <= timeout.Multiply(3.5), "Waited longer than " + timeout.Multiply(3.5) + ". Waited " + stopwatch.Elapsed);
            Assert.True(promise.Status == TaskStatus.Faulted);

            // 验证请求跟踪记录已清理，不应有挂起请求。
            Assert.Equal(expected: 0, actual: this.runtimeClient.GetRunningRequestsCount(errorGrainType));

            // 再次等待已超时的任务应立即失败。
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
        /// 测试 Grain 过载时丢弃调用的行为。
        /// 当 Grain 正忙于处理长时间运行的请求且客户端超时时，
        /// 对同一激活的后续调用可能被丢弃，以避免队列积压。
        /// 
        /// 场景：
        /// 1. 第一次调用耗时超过超时值，客户端收到 TimeoutException。
        /// 2. 第一次调用仍在运行时第二次调用到达，应被丢弃。
        /// 3. 验证 Grain 实际上只执行了第一次调用。
        /// 
        /// 由于问题 #3995，目前跳过此测试。
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

            // 第一次调用：耗时 5 秒，但客户端在 2 秒后超时。
            var delay = TimeSpan.FromSeconds(5);
            var firstCall = target.LongRunningTask(1, responseTimeout + delay);
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            // 第二次调用：由于 Grain 仍忙于第一次调用，应被丢弃。
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

            // 等待第一次调用在 Silo 上完成。
            await Task.Delay(delay, cancellationToken);

            // 验证只执行了第一次调用（值 = 1），第二次调用被丢弃。
            Assert.Equal(1, await target.GetLastValue());
        }
    }
}
