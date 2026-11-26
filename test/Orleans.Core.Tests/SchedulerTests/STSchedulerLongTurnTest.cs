using System.Diagnostics;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.SchedulerTests
{
    /// <summary>
    /// 测试 Orleans 排程器行为,进行长运行 grain 操作,以确保不会造成超时或队列阻塞。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestCategory("BVT")]
    [TestArea("Runtime")]
    public class STSchedulerLongTurnTest : HostedTestClusterEnsureDefaultStarted
    {
        public STSchedulerLongTurnTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional"), TestCategory("Scheduler")]
        public async Task Sched_LongTurnTest()
        {
            // 有了两根 silos,应该有16根线。
            // 我们能创造出比这更多的 grains 来确保我们把水池淹没
            var grains = new List<IErrorGrain>();
            var grainFullName = typeof(ErrorGrain).FullName;
            for (int i = 0; i < 100; i++)
            {
                grains.Add(this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName));
            }

            // 发送一堆无做请求, 只需启动 grains
            var promises = grains.Select(grain => grain.Dispose());
            await Task.WhenAll(promises);


            // 现在开始一个计时器, 然后排队 排成一堆长( 睡) 的要求
            var timer = new Stopwatch();
            timer.Start();

            promises = grains.Select(grain => grain.LongMethod(12));
            try
            {
                await Task.WhenAll(promises);
            }
            catch (Exception ex)
            {
                if (ex.GetBaseException() is TimeoutException)
                {
                    Assert.Fail("Long turns queued up and caused a timeout");
                }
                else
                {
                    throw;
                }
            }
            timer.Stop();

            Assert.True(timer.Elapsed.TotalSeconds < 40, "Long turns queued up and caused an extended runtime");
        }
    }
}
