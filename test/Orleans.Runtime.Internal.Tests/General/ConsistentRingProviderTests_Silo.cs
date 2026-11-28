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
    /// 测试 Silo 故障和加入时一致性哈希环提供程序的行为。
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
            foreach (SiloHandle fail in failures) // 故障前验证。
            {
                await VerificationScenario(PickKey(fail.SiloAddress), cancellationToken); // fail.SiloAddress.GetConsistentHashCode());
            }

            logger.LogInformation(
                "FailureTest {FailureCount}, Code {FailureCode}, Stopping silos: {SiloAddresses}",
                numOfFailures,
                failCode,
                Utils.EnumerableToString(failures, handle => handle.SiloAddress.ToString()));
            List<uint> keysToTest = new List<uint>();
            foreach (SiloHandle fail in failures) // 故障前验证。
            {
                keysToTest.Add(PickKey(fail.SiloAddress)); //fail.SiloAddress.GetConsistentHashCode());
                await this.HostedCluster.StopSiloAsync(fail, cancellationToken);
            }
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            await AssertEventually(async () =>
            {
                foreach (var key in keysToTest) // 故障后验证。
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
            uint keyToCheck = PickKey(failures[0].SiloAddress);// failures[0].SiloAddress.GetConsistentHashCode();

            // 并行终止一个 Silo 并加入一个新的 Silo。
            logger.LogInformation("Killing silo {SiloAddress} and joining a silo", failures[0].SiloAddress);

            var killTask = this.HostedCluster.StopSiloAsync(failures[0], cancellationToken);
            var joinTask = this.HostedCluster.StartAdditionalSilosAsync(1, false, cancellationToken);
            await Task.WhenAll([killTask, joinTask]).WaitAsync(endWait, cancellationToken);
            List<SiloHandle> joinedSilos = await joinTask;

            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            await AssertEventually(async () =>
            {
                await VerificationScenario(keyToCheck, cancellationToken); // 验证故障 Silo 的键。
                await VerificationScenario(PickKey(joinedSilos[0].SiloAddress), cancellationToken); // 验证新加入的 Silo 的键。
            }, failureTimeout, cancellationToken);
        }

        // 此场景中使次级 Silo 故障暴露了 DomainGrain 中的缺陷，因此将它与 Ring_1F1J 分开测试。
        [Fact, TestCategory("Functional"), TestCategory("Ring")]
        public async Task Ring_1Fsec1J()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await this.HostedCluster.StartAdditionalSilosAsync(numAdditionalSilos, false, cancellationToken);
            await this.HostedCluster.WaitForLivenessToStabilizeAsync();
            //List<SiloHandle> failures = getSilosToFail(Fail.Random, 1);
            SiloHandle fail = this.HostedCluster.SecondarySilos.First();
            uint keyToCheck = PickKey(fail.SiloAddress); //fail.SiloAddress.GetConsistentHashCode();

            // 并行终止一个 Silo 并加入一个新的 Silo。
            logger.LogInformation("Killing secondary silo {SiloAddress} and joining a silo", fail.SiloAddress);
            var killTask = this.HostedCluster.StopSiloAsync(fail, cancellationToken);
            var joinTask = this.HostedCluster.StartAdditionalSilosAsync(1, false, cancellationToken);
            await Task.WhenAll([killTask, joinTask]).WaitAsync(endWait, cancellationToken);
            var joinedSilo = (await joinTask).Single();

            await this.HostedCluster.WaitForLivenessToStabilizeAsync();

            await AssertEventually(async () =>
            {
                await VerificationScenario(keyToCheck, cancellationToken); // 验证故障 Silo 的键。
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
            // 准备测试环境。
            List<SiloAddress> silos = new List<SiloAddress>();

            foreach (var siloHandle in this.HostedCluster.GetActiveSilos())
            {
                long hash = siloHandle.SiloAddress.GetConsistentHashCode();
                int index = silos.FindLastIndex(siloAddr => siloAddr.GetConsistentHashCode() < hash) + 1;
                silos.Insert(index, siloHandle.SiloAddress);
            }

            // 验证参数中的键。
            await VerifyKey(testKey, silos, cancellationToken);
            // 除参数中的键外，还验证其他一些键。
            // 一些随机键。
            for (int i = 0; i < 3; i++)
            {
                await VerifyKey((uint)Random.Shared.Next(), silos, cancellationToken);
            }
            // 最小的键。
            uint lowest = (uint)(silos.First().GetConsistentHashCode() - 1);
            await VerifyKey(lowest, silos, cancellationToken);
            // 最大的键。
            uint highest = (uint)(silos.Last().GetConsistentHashCode() + 1);
            await VerifyKey(lowest, silos, cancellationToken);
        }

        private async Task VerifyKey(uint key, List<SiloAddress> silos, CancellationToken cancellationToken)
        {
            var testHooks = this.Client.GetTestHooks(this.HostedCluster.Primary!);
            SiloAddress truth = testHooks.GetConsistentRingPrimaryTargetSilo(key).Result; //expected;
            //if (truth == null) // if the truth isn't passed, we compute it here
            //{
            //    truth = silos.Find(siloAddr => (key <= siloAddr.GetConsistentHashCode()));
            //    if (truth == null)
            //    {
            //        truth = silos.First();
            //    }
            //}

            // 在所有 Silo 上查找 'key' 都应返回 'truth'。
            foreach (var siloHandle in this.HostedCluster.GetActiveSilos()) // 针对每个 Silo 执行此操作。
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

            // 确定主目录分区及托管 ReminderTableGrain 的 Silo。
            var tableGrain = this.GrainFactory.GetGrain<IReminderTableGrain>(InMemoryReminderTable.ReminderTableGrainId);
            var tableGrainId = ((GrainReference)tableGrain).GrainId;

            // 向 Grain 发送探测调用，确保其处于活跃状态。
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
                // 不要让主目录分区所在的 Silo 及托管 ReminderTableGrain 的 Silo 故障。
                if (siloAddress.Equals(reminderTableGrainPrimaryDirectoryAddress) || siloAddress.Equals(reminderGrainActivationSiloAddress))
                {
                    continue;
                }
                ids.Add(siloHandle.SiloAddress.GetConsistentHashCode(), siloHandle);
            }

            int index;
            // 不应使主 Silo 故障！
            // 如果 'Fail' 对应主 Silo 的地址，则无法保证其语义。
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
