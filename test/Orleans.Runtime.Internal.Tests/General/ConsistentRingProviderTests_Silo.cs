#nullable enable

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime.ReminderService;
using Orleans.TestingHost;
using TestExtensions;
using UnitTests.TestHelper;
using Xunit;
using Xunit.Sdk;

namespace UnitTests.General
{
    /// <summary>
    /// 测试在 silo 失败和加入期间, 一致环提供商的行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ConsistentRingProviderTests_Silo : TestClusterPerTest
    {
        private const int numAdditionalSilos = 3;
        private readonly TimeSpan failureTimeout = TimeSpan.FromSeconds(30);
        private readonly TimeSpan endWait = TimeSpan.FromMinutes(5);

        private enum Fail { First, Random, Last }

        protected override void ConfigureTestCluster(TestClusterBuilder builder)
        {
            builder.AddSiloBuilderConfigurator<Configurator>();
            builder.AddClientBuilderConfigurator<Configurator>();
        }

        private class Configurator : ISiloConfigurator, IClientBuilderConfigurator
        {
            public void Configure(ISiloBuilder hostBuilder)
            {
                hostBuilder.AddMemoryGrainStorage("MemoryStore")
                    .AddMemoryGrainStorageAsDefault()
                    .UseInMemoryReminderService();
            }

            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                clientBuilder.Configure<GatewayOptions>(
                    options => options.GatewayListRefreshPeriod = TimeSpan.FromMilliseconds(100));
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_Basic()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await this.HostedCluster.StartAdditionalSilosAsync(numAdditionalSilos, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            await VerificationScenario(0, cancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1F_Random()
        {
            await FailureTest(Fail.Random, 1, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1F_Beginning()
        {
            await FailureTest(Fail.First, 1, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1F_End()
        {
            await FailureTest(Fail.Last, 1, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_2F_Random()
        {
            await FailureTest(Fail.Random, 2, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_2F_Beginning()
        {
            await FailureTest(Fail.First, 2, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_2F_End()
        {
            await FailureTest(Fail.Last, 2, TestContext.Current.CancellationToken);
        }

        private async Task FailureTest(Fail failCode, int numOfFailures, CancellationToken cancellationToken)
        {
            await this.HostedCluster.StartAdditionalSilosAsync(numAdditionalSilos, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            List<SiloHandle> failures = await getSilosToFail(failCode, numOfFailures, cancellationToken);
            foreach (SiloHandle fail in failures) // 在未核实前
            {
                await VerificationScenario(PickKey(fail.SiloAddress), cancellationToken); // fail.SiloAddress.GetConsistentHashCode());
            }

            logger.LogInformation(
                "FailureTest {FailureCount}, Code {FailureCode}, Stopping silos: {SiloAddresses}",
                numOfFailures,
                failCode,
                Utils.EnumerableToString(failures, handle => handle.SiloAddress.ToString()));
            List<uint> keysToTest = new List<uint>();
            foreach (SiloHandle fail in failures) // 在未核实前
            {
                keysToTest.Add(PickKey(fail.SiloAddress)); //fail.SiloAddress.GetConsistentHashCode());
                await this.HostedCluster.StopSiloAsync(fail, cancellationToken);
            }
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            await AssertEventually(async () =>
            {
                foreach (var key in keysToTest) // 失败后核实
                {
                    await VerificationScenario(key, cancellationToken);
                }
            }, failureTimeout, cancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1J()
        {
            await JoinTest(1, TestContext.Current.CancellationToken);
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_2J()
        {
            await JoinTest(2, TestContext.Current.CancellationToken);
        }

        private async Task JoinTest(int numOfJoins, CancellationToken cancellationToken)
        {
            logger.LogInformation("JoinTest {NumOfJoins}", numOfJoins);
            await this.HostedCluster.StartAdditionalSilosAsync(numAdditionalSilos - numOfJoins, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            List<SiloHandle> silos = await this.HostedCluster.StartAdditionalSilosAsync(numOfJoins, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            foreach (SiloHandle sh in silos)
            {
                await VerificationScenario(PickKey(sh.SiloAddress), cancellationToken);
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1F1J()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await this.HostedCluster.StartAdditionalSilosAsync(numAdditionalSilos, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            List<SiloHandle> failures = await getSilosToFail(Fail.Random, 1, cancellationToken);
            uint keyToCheck = PickKey(failures[0].SiloAddress);// 失败 [0]。SiloAddress.GetConsistentHashCode();

            // 将一个 silo 切除并平行加入一个新的
            logger.LogInformation("Killing silo {SiloAddress} and joining a silo", failures[0].SiloAddress);

            var killTask = this.HostedCluster.StopSiloAsync(failures[0], cancellationToken);
            var joinTask = this.HostedCluster.StartAdditionalSilosAsync(1, false, cancellationToken);
            await Task.WhenAll([killTask, joinTask]).WaitAsync(endWait, cancellationToken);
            List<SiloHandle> joinedSilos = await joinTask;

            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            await AssertEventually(async () =>
            {
                await VerificationScenario(keyToCheck, cancellationToken); // 验证失败 silo的密钥
                await VerificationScenario(PickKey(joinedSilos[0].SiloAddress), cancellationToken); // 刚刚加入的 silo密钥
            }, failureTimeout, cancellationToken);
        }

        // 无法在二号机机失灵的情况下, 在 DomainGrain 中暴露了错误... 所以我们把它作为单独的测试 。
        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1Fsec1J()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await this.HostedCluster.StartAdditionalSilosAsync(numAdditionalSilos, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            //列出<SiloHandle> 故障= getSilosToFail(Fail.Random, 1);
            SiloHandle fail = this.HostedCluster.SecondarySilos.First();
            uint keyToCheck = PickKey(fail.SiloAddress); //fail.SiloAddress.GetConsistentHashCode();

            // 将一个 silo 切除并平行加入一个新的
            logger.LogInformation("Killing secondary silo {SiloAddress} and joining a silo", fail.SiloAddress);
            var killTask = this.HostedCluster.StopSiloAsync(fail, cancellationToken);
            var joinTask = this.HostedCluster.StartAdditionalSilosAsync(1, false, cancellationToken);
            await Task.WhenAll([killTask, joinTask]).WaitAsync(endWait, cancellationToken);
            var joinedSilo = (await joinTask).Single();

            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            await AssertEventually(async () =>
            {
                await VerificationScenario(keyToCheck, cancellationToken); // 验证失败 silo的密钥
                await VerificationScenario(PickKey(joinedSilo.SiloAddress), cancellationToken);
            }, failureTimeout, cancellationToken);
        }

        private uint PickKey(SiloAddress responsibleSilo)
        {
            int iteration = 10000;
            var testHooks = this.Client.GetTestHooks(this.HostedCluster.Primary!);
            for (int i = 0; i < iteration; i++)
            {
                double next = Random.Shared.NextDouble();
                uint randomKey = (uint)((double)RangeFactory.RING_SIZE * next);
                SiloAddress s = testHooks.GetConsistentRingPrimaryTargetSilo(randomKey).Result;
                if (responsibleSilo.Equals(s))
                    return randomKey;
            }
            throw new Exception(string.Format("Could not pick a key that silo {0} will be responsible for. Primary.Ring = \n{1}",
                responsibleSilo, testHooks.GetConsistentRingProviderDiagnosticInfo().Result));
        }

        private async Task VerificationScenario(uint testKey, CancellationToken cancellationToken)
        {
            // 设置设置设置
            List<SiloAddress> silos = new List<SiloAddress>();

            foreach (var siloHandle in this.HostedCluster.GetActiveSilos())
            {
                long hash = siloHandle.SiloAddress.GetConsistentHashCode();
                int index = silos.FindLastIndex(siloAddr => siloAddr.GetConsistentHashCode() < hash) + 1;
                silos.Insert(index, siloHandle.SiloAddress);
            }

            // 校验参数密钥
            await VerifyKey(testKey, silos, cancellationToken);
            // 校验其它一些密钥,除参数密钥外            
            // 一些随机密钥
            for (int i = 0; i < 3; i++)
            {
                await VerifyKey((uint)Random.Shared.Next(), silos, cancellationToken);
            }
            // 最低关键键
            uint lowest = (uint)(silos.First().GetConsistentHashCode() - 1);
            await VerifyKey(lowest, silos, cancellationToken);
            // 最高密钥
            uint highest = (uint)(silos.Last().GetConsistentHashCode() + 1);
            await VerifyKey(lowest, silos, cancellationToken);
        }

        private async Task VerifyKey(uint key, List<SiloAddress> silos, CancellationToken cancellationToken)
        {
            var testHooks = this.Client.GetTestHooks(this.HostedCluster.Primary!);
            SiloAddress truth = testHooks.GetConsistentRingPrimaryTargetSilo(key).Result; //预期;
            //如果真假真假不通过 我们在这里算
            //{
            //    = silos.Find(siloAddr ] * * * * (关键) * siloAddr.GetConsistentHashCode());
            //    (真象 =无)
            //    {
            //        真相 = silos.First();
            //    }
            //}

            // 查找“ key” 的“ 密钥” 应该返回全部 silos上的“ 真相”
            foreach (var siloHandle in this.HostedCluster.GetActiveSilos()) // 每一个都这样做 silo
            {
                testHooks = this.Client.GetTestHooks(siloHandle);
                SiloAddress s = await testHooks.GetConsistentRingPrimaryTargetSilo((uint)key).WaitAsync(cancellationToken);
                Assert.Equal(truth, s);
            }
        }

        private async Task<List<SiloHandle>> getSilosToFail(Fail fail, int numOfFailures, CancellationToken cancellationToken)
        {
            List<SiloHandle> failures = new List<SiloHandle>();
            int count = 0;

            // 解析主目录分区和 silo 主机主机 ReminderTableGrain.
            var tableGrain = this.GrainFactory.GetGrain<IReminderTableGrain>(InMemoryReminderTable.ReminderTableGrainId);
            var tableGrainId = ((GrainReference)tableGrain).GrainId;

            // 拨号 grain 以确保它被激活。
            await tableGrain.ReadRows(tableGrainId, cancellationToken);

            (SiloAddress reminderTableGrainPrimaryDirectoryAddress, SiloAddress reminderGrainActivationSiloAddress) =
                await GetReminderTableGrainAddresses(tableGrainId, cancellationToken);

            SortedList<int, SiloHandle> ids = new SortedList<int, SiloHandle>();
            foreach (var siloHandle in this.HostedCluster.GetActiveSilos())
            {
                SiloAddress siloAddress = siloHandle.SiloAddress;
                if (siloAddress.Equals(this.HostedCluster.Primary!.SiloAddress))
                {
                    continue;
                }
                // 不可失败主目录分区 silo 主机主机 ReminderTableGrain.
                if (siloAddress.Equals(reminderTableGrainPrimaryDirectoryAddress) || siloAddress.Equals(reminderGrainActivationSiloAddress))
                {
                    continue;
                }
                ids.Add(siloHandle.SiloAddress.GetConsistentHashCode(), siloHandle);
            }

            int index;
            // 我们不应该失败!
            // 我们不能保证“失败”的语义 如果它能以主地址为单位进行估价
            switch (fail)
            {
                case Fail.First:
                    index = 0;
                    while (count++ < numOfFailures)
                    {
                        while (failures.Contains(ids.Values[index]))
                        {
                            index++;
                        }
                        failures.Add(ids.Values[index]);
                    }
                    break;
                case Fail.Last:
                    index = ids.Count - 1;
                    while (count++ < numOfFailures)
                    {
                        while (failures.Contains(ids.Values[index]))
                        {
                            index--;
                        }
                        failures.Add(ids.Values[index]);
                    }
                    break;
                case Fail.Random:
                default:
                    while (count++ < numOfFailures)
                    {
                        SiloHandle r = ids.Values[Random.Shared.Next(ids.Count)];
                        while (failures.Contains(r))
                        {
                            r = ids.Values[Random.Shared.Next(ids.Count)];
                        }
                        failures.Add(r);
                    }
                    break;
            }
            return failures;
        }

        private async Task<(SiloAddress DirectoryAddress, SiloAddress ActivationSiloAddress)> GetReminderTableGrainAddresses(
            GrainId tableGrainId,
            CancellationToken cancellationToken)
        {
            SiloAddress? directoryAddress = null;
            SiloAddress? activationSiloAddress = null;

            await AssertEventually(async () =>
            {
                var primaryReport = await TestUtils.GetDetailedGrainReport(
                    this.HostedCluster.InternalGrainFactory!,
                    tableGrainId,
                    this.HostedCluster.Primary!).WaitAsync(cancellationToken);

                if (primaryReport.PrimaryForGrain is not { } primaryForGrain)
                {
                    throw new XunitException($"No primary directory silo was reported for reminder table grain {tableGrainId}.");
                }

                var directorySilo = this.HostedCluster.GetSiloForAddress(primaryForGrain);
                if (directorySilo is null)
                {
                    throw new XunitException($"No active silo handle was found for reminder table grain directory silo {primaryForGrain}.");
                }

                var directoryReport = await TestUtils.GetDetailedGrainReport(
                    this.HostedCluster.InternalGrainFactory!,
                    tableGrainId,
                    directorySilo).WaitAsync(cancellationToken);

                if (directoryReport.LocalDirectoryActivationAddress is not { } activationAddress)
                {
                    throw new XunitException($"No activation address was reported for reminder table grain {tableGrainId} from directory silo {primaryForGrain}.");
                }

                directoryAddress = primaryForGrain;
                activationSiloAddress = activationAddress.SiloAddress;
            }, failureTimeout, cancellationToken);

            return (
                directoryAddress ?? throw new XunitException($"No primary directory silo was reported for reminder table grain {tableGrainId}."),
                activationSiloAddress ?? throw new XunitException($"No activation silo was reported for reminder table grain {tableGrainId}."));
        }

        private static async Task AssertEventually(Func<Task> assertion, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await AssertEventually(assertion, timeout, TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        private static async Task AssertEventually(
            Func<Task> assertion,
            TimeSpan timeout,
            TimeSpan delayBetweenIterations,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await assertion();
                    return;
                }
                catch (XunitException)
                {
                    if (sw.ElapsedMilliseconds > timeout.TotalMilliseconds)
                    {
                        throw;
                    }
                }

                if (delayBetweenIterations > TimeSpan.Zero)
                {
                    await Task.Delay(delayBetweenIterations, cancellationToken);
                }
            }
        }
    }
}
