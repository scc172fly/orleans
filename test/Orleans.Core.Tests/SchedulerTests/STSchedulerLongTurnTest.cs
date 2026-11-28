using System.Diagnostics;
using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.SchedulerTests
{
    /// <summary>
    /// 测试长时间运行的 Grain 操作下 Orleans 调度器的行为，确保这些操作不会导致超时或队列阻塞。
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
            // 两个 Silo 应有 16 个线程。
            // 创建远多于此数量的 Grain，以确保线程池处于饱和状态
            var grains = new List<IErrorGrain>();
            var grainFullName = typeof(ErrorGrain).FullName;
            for (int i = 0; i < 100; i++)
            {
                grains.Add(this.GrainFactory.GetGrain<IErrorGrain>(GetRandomGrainId(), grainFullName));
            }

            // 发送一批不执行任何操作的请求，仅用于激活 Grain
            var promises = grains.Select(grain => grain.Dispose());
            await Task.WhenAll(promises);


            // 现在启动计时器，然后将一批长时间运行（休眠）的请求排入队列
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
