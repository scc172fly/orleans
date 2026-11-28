using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Runtime.Diagnostics;
using Orleans.Serialization.TypeSystem;
using Orleans.TestingHost;
using Tester;
using TestExtensions;
using UnitTestGrains;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.ActivationsLifeCycleTests
{
    /// <summary>
    /// 测试负责管理 Grain 激活生命周期及垃圾回收的激活回收器。
    /// </summary>
    [TestArea("Runtime")]
    public class ActivationCollectorTests : OrleansTestingBase, IAsyncLifetime
    {
        private static readonly TimeSpan DEFAULT_COLLECTION_QUANTUM = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan DEFAULT_IDLE_TIMEOUT = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan WAIT_TIME = DEFAULT_IDLE_TIMEOUT + DEFAULT_COLLECTION_QUANTUM.Multiply(2.0);
        private static readonly TimeSpan COLLECTION_SPECIFIC_AGE_LIMIT = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan FORCED_COLLECTION_AGE_LIMIT = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan FORCED_COLLECTION_WAIT_TIME = FORCED_COLLECTION_AGE_LIMIT + TimeSpan.FromMilliseconds(250);

        private TestCluster testCluster = null!;

        private ILogger logger = null!;

        private async Task Initialize(TimeSpan collectionAgeLimit, TimeSpan quantum, CancellationToken cancellationToken)
        {
            var builder = new TestClusterBuilder(1);
            builder.Properties["CollectionQuantum"] = quantum.ToString();
            builder.Properties["DefaultCollectionAgeLimit"] = collectionAgeLimit.ToString();
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            testCluster = builder.Build();
            await testCluster.DeployAsync(cancellationToken);
            this.logger = this.testCluster.Client!.ServiceProvider.GetRequiredService<ILogger<ActivationCollectorTests>>();
        }

        public class SiloConfigurator : IHostConfigurator
        {
            public void Configure(IHostBuilder hostBuilder)
            {
                var config = hostBuilder.GetConfiguration();
                var collectionAgeLimit = TimeSpan.Parse(config["DefaultCollectionAgeLimit"]!);
                var quantum = TimeSpan.Parse(config["CollectionQuantum"]!);
                hostBuilder.UseOrleans((ctx, siloBuilder) =>
                {
                    siloBuilder
                        .ConfigureServices(services => services.Where(s => s.ServiceType == typeof(IConfigurationValidator)).ToList().ForEach(s => services.Remove(s)));
                    siloBuilder.Configure<GrainCollectionOptions>(options =>
                    {
                        options.CollectionAge = collectionAgeLimit;
                        options.CollectionQuantum = quantum;
                        options.ClassSpecificCollectionAge = new Dictionary<string, TimeSpan>
                        {
                            [typeof(IdleActivationGcTestGrain2).FullName!] = DEFAULT_IDLE_TIMEOUT,
                            [typeof(BusyActivationGcTestGrain2).FullName!] = DEFAULT_IDLE_TIMEOUT,
                            [typeof(CollectionSpecificAgeLimitForTenSecondsActivationGcTestGrain).FullName!] = COLLECTION_SPECIFIC_AGE_LIMIT,
                        };
                    });
                });
            }
        }


        ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

        private async Task Initialize(TimeSpan collectionAgeLimit, CancellationToken cancellationToken)
        {
            await Initialize(collectionAgeLimit, DEFAULT_COLLECTION_QUANTUM, cancellationToken);
        }

        private static async Task CancelWorkerAsync(CancellationTokenSource cancellation, Task worker)
        {
            cancellation.Cancel();
            try
            {
                await worker;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (testCluster is null) return;

            try
            {
                await testCluster.StopAllSilosAsync();
            }
            finally
            {
                await testCluster.DisposeAsync();
                testCluster = null!;
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorForceCollection()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            const int grainCount = 1000;
            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(IdleActivationGcTestGrain1));

            List<Task> tasks = new List<Task>();
            logger.LogInformation("ActivationCollectorForceCollection: activating {Count} grains.", grainCount);
            for (var i = 0; i < grainCount; ++i)
            {
                IIdleActivationGcTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IIdleActivationGcTestGrain1>(Guid.NewGuid());
                tasks.Add(g.Nop());
            }
            await Task.WhenAll(tasks);

            await Task.Delay(FORCED_COLLECTION_WAIT_TIME, cancellationToken);

            var grain = this.testCluster.GrainFactory!.GetGrain<IManagementGrain>(0);

            using var collectionObserver = ActivationCollectionObserver.Create();
            await grain.ForceActivationCollection(FORCED_COLLECTION_AGE_LIMIT, cancellationToken);
            await collectionObserver.WaitForCollectedCountAsync(
                fullGrainTypeName,
                grainCount,
                DeactivationReasonCode.ActivationIdle,
                TestConstants.InitTimeout,
                cancellationToken);

            int activationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(0, activationsNotCollected);

            await grain.ForceActivationCollection(FORCED_COLLECTION_AGE_LIMIT, cancellationToken);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldCollectIdleActivations()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            const int grainCount = 1000;
            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(IdleActivationGcTestGrain1));

            List<Task> tasks = new List<Task>();
            logger.LogInformation("IdleActivationCollectorShouldCollectIdleActivations: activating {Count} grains.", grainCount);
            for (var i = 0; i < grainCount; ++i)
            {
                IIdleActivationGcTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IIdleActivationGcTestGrain1>(Guid.NewGuid());
                tasks.Add(g.Nop());
            }
            await Task.WhenAll(tasks);

            int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(grainCount, activationsCreated);

            logger.LogInformation(
                "IdleActivationCollectorShouldCollectIdleActivations: grains activated; waiting for activation count to converge to zero (activation GC idle timeout is {DefaultIdleTime} sec).",
                DEFAULT_IDLE_TIMEOUT.TotalSeconds);
            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);

            int activationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(0, activationsNotCollected);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldNotCollectBusyActivations()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            const int idleGrainCount = 500;
            const int busyGrainCount = 500;
            var idleGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(IdleActivationGcTestGrain1));
            var busyGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(BusyActivationGcTestGrain1));

            List<Task> tasks0 = new List<Task>();
            List<IBusyActivationGcTestGrain1> busyGrains = new List<IBusyActivationGcTestGrain1>();
            logger.LogInformation("ActivationCollectorShouldNotCollectBusyActivations: activating {Count} busy grains.", busyGrainCount);
            for (var i = 0; i < busyGrainCount; ++i)
            {
                IBusyActivationGcTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IBusyActivationGcTestGrain1>(Guid.NewGuid());
                busyGrains.Add(g);
                tasks0.Add(g.Nop());
            }
            await Task.WhenAll(tasks0);
            using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            async Task busyWorker(CancellationToken cancellationToken)
            {
                logger.LogInformation("ActivationCollectorShouldNotCollectBusyActivations: busyWorker started");
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<Task> tasks1 = new List<Task>(busyGrains.Count);
                    foreach (var g in busyGrains)
                        tasks1.Add(g.Nop());
                    await Task.WhenAll(tasks1);
                }
            }
            var workerTask = Task.Run(() => busyWorker(workerCancellation.Token), cancellationToken);
            try
            {
                logger.LogInformation("ActivationCollectorShouldNotCollectBusyActivations: activating {Count} idle grains.", idleGrainCount);
                tasks0.Clear();
                for (var i = 0; i < idleGrainCount; ++i)
                {
                    IIdleActivationGcTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IIdleActivationGcTestGrain1>(Guid.NewGuid());
                    tasks0.Add(g.Nop());
                }
                await Task.WhenAll(tasks0);

                int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, idleGrainTypeName) + await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, busyGrainTypeName);
                Assert.Equal(idleGrainCount + busyGrainCount, activationsCreated);

                logger.LogInformation(
                    "ActivationCollectorShouldNotCollectBusyActivations: grains activated; waiting for idle activation count to converge to zero (activation GC idle timeout is {DefaultIdleTime} sec).",
                    DEFAULT_IDLE_TIMEOUT.TotalSeconds);
                await WaitForActivationCountToConverge(idleGrainTypeName, expectedCount: 0, cancellationToken);

                // 应当只回收空闲类别（IdleActivationGcTestGrain1）中的 Grain。
                int idleActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, idleGrainTypeName);
                int busyActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, busyGrainTypeName);
                Assert.Equal(0, idleActivationsNotCollected);
                Assert.Equal(busyGrainCount, busyActivationsNotCollected);
            }
            finally
            {
                await CancelWorkerAsync(workerCancellation, workerTask);
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ManualCollectionShouldNotCollectBusyActivations()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(TimeSpan.FromHours(1), cancellationToken);

            var manualCollectionWaitTime = FORCED_COLLECTION_WAIT_TIME;
            const int idleGrainCount = 500;
            const int busyGrainCount = 500;
            var idleGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(IdleActivationGcTestGrain1));
            var busyGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(BusyActivationGcTestGrain1));

            List<Task> tasks0 = new List<Task>();
            List<IBusyActivationGcTestGrain1> busyGrains = new List<IBusyActivationGcTestGrain1>();
            logger.LogInformation("ManualCollectionShouldNotCollectBusyActivations: activating {Count} busy grains.", busyGrainCount);
            for (var i = 0; i < busyGrainCount; ++i)
            {
                IBusyActivationGcTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IBusyActivationGcTestGrain1>(Guid.NewGuid());
                busyGrains.Add(g);
                tasks0.Add(g.Nop());
            }
            await Task.WhenAll(tasks0);

            logger.LogInformation("ManualCollectionShouldNotCollectBusyActivations: activating {Count} idle grains.", idleGrainCount);
            tasks0.Clear();
            for (var i = 0; i < idleGrainCount; ++i)
            {
                IIdleActivationGcTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IIdleActivationGcTestGrain1>(Guid.NewGuid());
                tasks0.Add(g.Nop());
            }
            await Task.WhenAll(tasks0);

            int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, idleGrainTypeName) + await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, busyGrainTypeName);
            Assert.Equal(idleGrainCount + busyGrainCount, activationsCreated);

            logger.LogInformation(
                "ManualCollectionShouldNotCollectBusyActivations: grains activated; waiting {TotalSeconds} sec before manual collection.",
                manualCollectionWaitTime.TotalSeconds);
            await Task.Delay(manualCollectionWaitTime, cancellationToken);

            using var callBarrier = BusyActivationGcTestGrain1.ArmBlockingCallBarrier(busyGrainCount);
            var busyCalls = busyGrains.Select(grain => grain.BlockUntilReleased()).ToArray();
            try
            {
                await callBarrier.WaitForParticipantsAsync(TestConstants.InitTimeout, cancellationToken);

                logger.LogInformation("ManualCollectionShouldNotCollectBusyActivations: triggering manual collection (age limit is {TotalSeconds} sec).", FORCED_COLLECTION_AGE_LIMIT.TotalSeconds);
                IManagementGrain mgmtGrain = this.testCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
                await mgmtGrain.ForceActivationCollection(FORCED_COLLECTION_AGE_LIMIT, cancellationToken);

                int idleActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, idleGrainTypeName);
                int busyActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, busyGrainTypeName);
                Assert.Equal(0, idleActivationsNotCollected);
                Assert.Equal(busyGrainCount, busyActivationsNotCollected);
            }
            finally
            {
                callBarrier.Release();
                await Task.WhenAll(busyCalls).WaitAsync(TestConstants.InitTimeout, cancellationToken);
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldCollectIdleActivationsSpecifiedInPerTypeConfiguration()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            //确保默认值不会导致激活在等待期间被回收。
            var defaultCollectionAgeLimit = WAIT_TIME.Multiply(2);
            await Initialize(defaultCollectionAgeLimit, cancellationToken);

            const int grainCount = 1000;
            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(IdleActivationGcTestGrain2));

            List<Task> tasks = new List<Task>();
            logger.LogInformation("ActivationCollectorShouldCollectIdleActivationsSpecifiedInPerTypeConfiguration: activating {Count} grains.", grainCount);
            for (var i = 0; i < grainCount; ++i)
            {
                IIdleActivationGcTestGrain2 g = this.testCluster.GrainFactory!.GetGrain<IIdleActivationGcTestGrain2>(Guid.NewGuid());
                tasks.Add(g.Nop());
            }
            await Task.WhenAll(tasks);

            int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(grainCount, activationsCreated);

            logger.LogInformation(
                "ActivationCollectorShouldCollectIdleActivationsSpecifiedInPerTypeConfiguration: grains activated; waiting for activation count to converge to zero (activation GC idle timeout is {DefaultIdleTime} sec).",
                DEFAULT_IDLE_TIMEOUT.TotalSeconds);
            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);

            int activationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(0, activationsNotCollected);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldNotCollectBusyActivationsSpecifiedInPerTypeConfiguration()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            //确保默认值不会导致激活在等待期间被回收。
            var defaultCollectionAgeLimit = WAIT_TIME.Multiply(2);
            await Initialize(defaultCollectionAgeLimit, cancellationToken);

            const int idleGrainCount = 500;
            const int busyGrainCount = 500;
            var idleGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(IdleActivationGcTestGrain2));
            var busyGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(BusyActivationGcTestGrain2));

            List<Task> tasks0 = new List<Task>();
            List<IBusyActivationGcTestGrain2> busyGrains = new List<IBusyActivationGcTestGrain2>();
            logger.LogInformation("ActivationCollectorShouldNotCollectBusyActivationsSpecifiedInPerTypeConfiguration: activating {Count} busy grains.", busyGrainCount);
            for (var i = 0; i < busyGrainCount; ++i)
            {
                IBusyActivationGcTestGrain2 g = this.testCluster.GrainFactory!.GetGrain<IBusyActivationGcTestGrain2>(Guid.NewGuid());
                busyGrains.Add(g);
                tasks0.Add(g.Nop());
            }
            await Task.WhenAll(tasks0);
            using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            async Task busyWorker(CancellationToken cancellationToken)
            {
                logger.LogInformation("ActivationCollectorShouldNotCollectBusyActivationsSpecifiedInPerTypeConfiguration: busyWorker started");
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<Task> tasks1 = new List<Task>(busyGrains.Count);
                    foreach (var g in busyGrains)
                        tasks1.Add(g.Nop());
                    await Task.WhenAll(tasks1);
                }
            }
            var workerTask = Task.Run(() => busyWorker(workerCancellation.Token), cancellationToken);
            try
            {
                logger.LogInformation("ActivationCollectorShouldNotCollectBusyActivationsSpecifiedInPerTypeConfiguration: activating {Count} idle grains.", idleGrainCount);
                tasks0.Clear();
                for (var i = 0; i < idleGrainCount; ++i)
                {
                    IIdleActivationGcTestGrain2 g = this.testCluster.GrainFactory!.GetGrain<IIdleActivationGcTestGrain2>(Guid.NewGuid());
                    tasks0.Add(g.Nop());
                }
                await Task.WhenAll(tasks0);

                int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, idleGrainTypeName) + await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, busyGrainTypeName);
                Assert.Equal(idleGrainCount + busyGrainCount, activationsCreated);

                logger.LogInformation(
                    "IdleActivationCollectorShouldNotCollectBusyActivations: grains activated; waiting for idle activation count to converge to zero (activation GC idle timeout is {DefaultIdleTime} sec).",
                    DEFAULT_IDLE_TIMEOUT.TotalSeconds);
                await WaitForActivationCountToConverge(idleGrainTypeName, expectedCount: 0, cancellationToken);

                // 应当只回收空闲类别（IdleActivationGcTestGrain2）中的 Grain。
                int idleActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, idleGrainTypeName);
                int busyActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, busyGrainTypeName);
                Assert.Equal(0, idleActivationsNotCollected);
                Assert.Equal(busyGrainCount, busyActivationsNotCollected);
            }
            finally
            {
                await CancelWorkerAsync(workerCancellation, workerTask);
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact(Skip = "Flaky test. Needs to be investigated."), TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldNotCollectBusyStatelessWorkers()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            // 此测试旨在确定激活回收器能否正确识别空闲的无状态工作者激活。
            // 在此测试中，我们：
            //
            //   1. 准备测试环境。
            //   2. 向每个 Grain 突发发送一批消息，以激活一组 Grain。突发发送旨在确保使用多个激活。
            //   3. 验证每个 Grain 均已创建多个激活。
            //   4. 定期向每个 Grain 发送消息，确保只有一个激活保持忙碌。每次检查激活 ID，并将其与上一次 Grain 调用返回的激活 ID 比较。最初它们可能不一致，但随着其他激活进入空闲状态并被回收，最终只剩一个激活处理这些调用。
            //   5. 等待足够长的时间，使空闲激活被回收。
            //   6. 验证每个 Grain 仍只有一个活跃的激活。
            //   7. 确保测试步骤 2 至 6 可以重复执行。

            const int grainCount = 1;
            var grainTypeName = RuntimeTypeNameFormatter.Format(typeof(StatelessWorkerActivationCollectorTestGrain1));
            const int burstLength = 1000;

            List<Task> tasks0 = new List<Task>();
            List<IStatelessWorkerActivationCollectorTestGrain1> grains = new List<IStatelessWorkerActivationCollectorTestGrain1>();
            for (var i = 0; i < grainCount; ++i)
            {
                IStatelessWorkerActivationCollectorTestGrain1 g = this.testCluster.GrainFactory!.GetGrain<IStatelessWorkerActivationCollectorTestGrain1>(Guid.NewGuid());
                grains.Add(g);
            }


            bool[] quit = new bool[] { false };
            bool[] matched = new bool[grainCount];
            string[] activationIds = new string[grainCount];
            async Task workFunc(int index, CancellationToken cancellationToken)
            {
                // （部分）4. 定期向每个 Grain 发送消息……

                // 选取一个 Grain 并调用 Delay，使其保持忙碌。
                IStatelessWorkerActivationCollectorTestGrain1 g = grains[index];
                await g.Delay(DEFAULT_IDLE_TIMEOUT.Divide(2)).WaitAsync(cancellationToken);
                // 识别激活并记录其是否与上次报告的激活 ID 相同。起初可能不匹配，但随着其他激活被回收，应当最终趋于一致。
                string aid = await g.IdentifyActivation();
                logger.LogInformation("ActivationCollectorShouldNotCollectBusyStatelessWorkers: identified {Activation}", aid);
                matched[index] = aid == activationIds[index];
                activationIds[index] = aid;
            }
            async Task workerFunc(CancellationToken cancellationToken)
            {
                // （部分）4. 定期向每个 Grain 发送消息……
                logger.LogInformation("ActivationCollectorShouldNotCollectBusyStatelessWorkers: busyWorker started");

                List<Task> tasks1 = new List<Task>();
                while (!quit[0])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int index = 0; index < grains.Count; ++index)
                    {
                        if (quit[0])
                        {
                            break;
                        }

                        tasks1.Add(workFunc(index, cancellationToken));
                    }
                    await Task.WhenAll(tasks1);
                }
            }

            // 准备阶段（1）到此结束。

            for (int i = 0; i < 2; ++i)
            {
                // 2. 激活一组 Grain……
                this.logger.LogInformation("ActivationCollectorShouldNotCollectBusyStatelessWorkers: activating {Count} stateless worker grains (run #{RunNumber}).", grainCount, i);
                foreach (var g in grains)
                {
                    for (int j = 0; j < burstLength; ++j)
                    {
                        // 让激活延迟处理，确保单个激活无法处理我们发送的全部请求，从而创建额外激活。
                        tasks0.Add(g.Delay(TimeSpan.FromMilliseconds(10)));
                    }
                }
                await Task.WhenAll(tasks0);


                // 3. 验证每个 Grain 均已创建多个激活。
                int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, grainTypeName);
                Assert.True(activationsCreated > grainCount, $"more than {grainCount} activations should have been created; got {activationsCreated} instead");

                // 4. 定期向每个 Grain 发送消息……
                this.logger.LogInformation("ActivationCollectorShouldNotCollectBusyStatelessWorkers: grains activated; sending heartbeat to {Count} stateless worker grains.", grainCount);
                Task workerTask = Task.Run(() => workerFunc(cancellationToken), cancellationToken);

                // 5. 等待足够长的时间，使空闲激活被回收。
                this.logger.LogInformation(
                    "ActivationCollectorShouldNotCollectBusyStatelessWorkers: grains activated; waiting {WaitSeconds} sec (activation GC idle timeout is {DefaultIdleTimeout} sec).",
                    WAIT_TIME.TotalSeconds,
                    DEFAULT_IDLE_TIMEOUT.TotalSeconds);
                await Task.Delay(WAIT_TIME, cancellationToken);

                // 6. 验证每个 Grain 仍只有一个活跃的激活。
                int busyActivationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, grainTypeName);

                // 发出信号通知工作任务停止，并等待其结束。
                quit[0] = true;
                await workerTask.WaitAsync(cancellationToken);
                quit[0] = false;

                Assert.Equal(grainCount, busyActivationsNotCollected);

                // 验证步骤 4 循环的最后一次迭代中激活 ID 相匹配。
                for (int index = 0; index < grains.Count; ++index)
                {
                    Assert.True(matched[index], string.Format("activation ID of final subsequent heartbeats did not match for grain {0}", grains[index]));
                }
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldCollectByCollectionSpecificAgeLimit()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var defaultCollectionAge = COLLECTION_SPECIFIC_AGE_LIMIT.Multiply(4);
            //确保 defaultCollectionAge 的值不会导致激活在达到类型专属限制之前被回收。
            await Initialize(defaultCollectionAge, cancellationToken);

            const int grainCount = 1000;

            // CollectionAgeLimit 按类型配置。
            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(CollectionSpecificAgeLimitForTenSecondsActivationGcTestGrain));

            List<Task> tasks = new List<Task>();
            logger.LogInformation("ActivationCollectorShouldCollectByCollectionSpecificAgeLimit: activating {GrainCount} grains.", grainCount);
            for (var i = 0; i < grainCount; ++i)
            {
                ICollectionSpecificAgeLimitForTenSecondsActivationGcTestGrain g = this.testCluster.GrainFactory!.GetGrain<ICollectionSpecificAgeLimitForTenSecondsActivationGcTestGrain>(Guid.NewGuid());
                tasks.Add(g.Nop());
            }
            await Task.WhenAll(tasks);

            int activationsCreated = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(grainCount, activationsCreated);

            logger.LogInformation(
                "ActivationCollectorShouldCollectByCollectionSpecificAgeLimit: grains activated; waiting for activation count to converge to zero using class-specific age limit {CollectionSpecificAgeLimit} sec.",
                COLLECTION_SPECIFIC_AGE_LIMIT.TotalSeconds);

            var activationCollector = this.testCluster.GetSiloServiceProvider().GetRequiredService<ActivationCollector>();
            await activationCollector.CollectStaleActivations(cancellationToken);
            int activationsAfterDefaultCollection = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(grainCount, activationsAfterDefaultCollection);

            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);
        }

        private async Task WaitForActivationCountToConverge(
            string grainTypeName,
            int expectedCount,
            CancellationToken cancellationToken)
        {
            using var collectionObserver = ActivationCollectionObserver.Create();
            var activationCount = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, grainTypeName);
            if (activationCount == expectedCount)
            {
                return;
            }

            var expectedCollectedCount = activationCount - expectedCount;
            Assert.True(expectedCollectedCount > 0, $"Expected activation count for {grainTypeName} to decrease from {activationCount} to {expectedCount}.");

            await collectionObserver.WaitForCollectedCountAsync(
                grainTypeName,
                expectedCollectedCount,
                DeactivationReasonCode.ActivationIdle,
                TestConstants.InitTimeout,
                cancellationToken);

            activationCount = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, grainTypeName);
            Assert.Equal(expectedCount, activationCount);
        }

        private sealed class ActivationCollectionObserver : IDisposable, IObserver<ActivationCollectionEvents.CollectionEvent>
        {
            private readonly object _lock = new();
            private readonly List<ActivationCollectionEvents.CollectionCompleted> _completedEvents = [];
            private readonly List<Waiter> _waiters = [];
            private IDisposable _subscription = null!;

            public static ActivationCollectionObserver Create()
            {
                var observer = new ActivationCollectionObserver();
                observer._subscription = ActivationCollectionEvents.AllEvents.Subscribe(observer);
                return observer;
            }

            public async Task WaitForCollectedCountAsync(
                string grainTypeName,
                int expectedCount,
                DeactivationReasonCode reasonCode,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                var waiter = new Waiter(grainTypeName, expectedCount, reasonCode);
                lock (_lock)
                {
                    if (GetCollectedCount(waiter) >= expectedCount)
                    {
                        return;
                    }

                    _waiters.Add(waiter);
                }

                using var timeoutCancellation = new CancellationTokenSource(timeout);
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeoutCancellation.Token, cancellationToken);
                using var registration = cancellation.Token.Register(static state => ((Waiter)state!).TrySetCanceled(), waiter);

                try
                {
                    await waiter.Task;
                }
                catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    var count = GetCollectedCount(waiter);
                    throw new TimeoutException($"Timed out waiting for {expectedCount} collected activations of grain type '{grainTypeName}' with reason {reasonCode}. Current count: {count} after {timeout}.");
                }
                finally
                {
                    lock (_lock)
                    {
                        _waiters.Remove(waiter);
                    }
                }
            }

            void IObserver<ActivationCollectionEvents.CollectionEvent>.OnNext(ActivationCollectionEvents.CollectionEvent value)
            {
                if (value is not ActivationCollectionEvents.CollectionCompleted completed)
                {
                    return;
                }

                lock (_lock)
                {
                    _completedEvents.Add(completed);
                    foreach (var waiter in _waiters)
                    {
                        if (GetCollectedCount(waiter) >= waiter.ExpectedCount)
                        {
                            waiter.TrySetResult();
                        }
                    }

                    _waiters.RemoveAll(static waiter => waiter.IsCompleted);
                }
            }

            void IObserver<ActivationCollectionEvents.CollectionEvent>.OnError(Exception error)
            {
            }

            void IObserver<ActivationCollectionEvents.CollectionEvent>.OnCompleted()
            {
            }

            public void Dispose()
            {
                _subscription?.Dispose();
            }

            private int GetCollectedCount(Waiter waiter)
            {
                lock (_lock)
                {
                    var result = 0;
                    foreach (var completed in _completedEvents)
                    {
                        if (completed.Reason.ReasonCode != waiter.ReasonCode)
                        {
                            continue;
                        }

                        foreach (var activation in completed.Activations)
                        {
                            if (MatchesGrainType(activation.GrainId, waiter.GrainTypeName))
                            {
                                result++;
                            }
                        }
                    }

                    return result;
                }
            }

            private static bool MatchesGrainType(GrainId grainId, string grainTypeName)
            {
                var eventGrainTypeName = grainId.Type.ToString();
                return eventGrainTypeName.Contains(grainTypeName, StringComparison.OrdinalIgnoreCase)
                    || grainTypeName.Contains(eventGrainTypeName, StringComparison.OrdinalIgnoreCase);
            }

            private sealed class Waiter(string grainTypeName, int expectedCount, DeactivationReasonCode reasonCode)
            {
                private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

                public string GrainTypeName { get; } = grainTypeName;
                public int ExpectedCount { get; } = expectedCount;
                public DeactivationReasonCode ReasonCode { get; } = reasonCode;
                public Task Task => _completion.Task;
                public bool IsCompleted => _completion.Task.IsCompleted;
                public void TrySetResult() => _completion.TrySetResult();
                public void TrySetCanceled() => _completion.TrySetCanceled();
            }
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldCollectAfterCancellingKeepAlive()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(KeepAliveActivationGcTestGrain));

            // 激活 Grain 并将其生存期延长至 5 分钟。
            var grain = this.testCluster.GrainFactory!.GetGrain<IKeepAliveActivationGcTestGrain>(Guid.NewGuid());
            await grain.SetKeepAlive(TimeSpan.FromMinutes(5));

            // 验证 Grain 在保活机制有效期间不会被回收。
            var mgmtGrain = this.testCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            await mgmtGrain.ForceActivationCollection(TimeSpan.Zero, cancellationToken);
            int activationsWithKeepAlive = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(1, activationsWithKeepAlive);

            // 取消保活。现在 Grain 应可在标准空闲超时后被回收。
            await grain.CancelKeepAlive();

            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);

            int activationsNotCollected = await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName);
            Assert.Equal(0, activationsNotCollected);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldCollectAfterReleasingKeepAliveHolds()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(KeepAliveActivationGcTestGrain));
            var grain = this.testCluster.GrainFactory!.GetGrain<IKeepAliveActivationGcTestGrain>(Guid.NewGuid());
            await grain.AcquireKeepAliveHold();
            await grain.AcquireKeepAliveHold();

            // 持有操作采用计数机制，因此所有持有均释放后激活才不再保持存活。
            var mgmtGrain = this.testCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            await mgmtGrain.ForceActivationCollection(TimeSpan.Zero, cancellationToken);
            Assert.Equal(1, await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName));

            await grain.ReleaseKeepAliveHold();
            await mgmtGrain.ForceActivationCollection(TimeSpan.Zero, cancellationToken);
            Assert.Equal(1, await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName));

            await grain.ReleaseKeepAliveHold();
            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);
            Assert.Equal(0, await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName));
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("ActivationCollector"), TestCategory("Functional")]
        public async Task ActivationCollectorShouldRestartIdlePeriodWhenLastKeepAliveHoldIsReleased()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(KeepAliveActivationGcTestGrain));
            var grain = this.testCluster.GrainFactory!.GetGrain<IKeepAliveActivationGcTestGrain>(Guid.NewGuid());
            await grain.AcquireKeepAliveHold();

            var directory = ((InProcessSiloHandle)this.testCluster.Primary!).SiloHost.Services.GetRequiredService<ActivationDirectory>();
            var keepAlive = Assert.IsAssignableFrom<IActivationKeepAlive>(directory.FindTarget(((GrainReference)grain).GrainId));

            // 最后一个请求后开始的空闲期在仍被持有时到期。
            await Task.Delay(DEFAULT_IDLE_TIMEOUT + DEFAULT_COLLECTION_QUANTUM, cancellationToken);
            var mgmtGrain = this.testCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            await mgmtGrain.ForceActivationCollection(DEFAULT_IDLE_TIMEOUT, cancellationToken);
            Assert.Equal(1, await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName));

            // 在请求处理之外释放最后一次持有时，开始新的空闲期。
            keepAlive.ReleaseKeepAlive();
            await mgmtGrain.ForceActivationCollection(DEFAULT_IDLE_TIMEOUT, cancellationToken);
            Assert.Equal(1, await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName));

            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Theory, TestCategory("ActivationCollector"), TestCategory("Functional")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RunRequestKeepAliveOptionControlsIdlePeriod(bool keepAlive)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(KeepAliveActivationGcTestGrain));
            var grain = this.testCluster.GrainFactory!.GetGrain<IKeepAliveActivationGcTestGrain>(Guid.NewGuid());
            var requestDuration = TimeSpan.FromSeconds(2);
            await grain.QueueRequest(requestDuration, keepAlive);

            var directory = ((InProcessSiloHandle)this.testCluster.Primary!).SiloHost.Services.GetRequiredService<ActivationDirectory>();
            var activation = Assert.IsType<ActivationData>(directory.FindTarget(((GrainReference)grain).GrainId));
            while (!activation.IsInactive)
            {
                await Task.Delay(10, cancellationToken);
            }

            // 除非请求重新开始计时，否则空闲期从此前 Grain 调用完成时开始，早于此请求执行。
            var mgmtGrain = this.testCluster.GrainFactory!.GetGrain<IManagementGrain>(0);
            await mgmtGrain.ForceActivationCollection(TimeSpan.FromSeconds(1), cancellationToken);
            Assert.Equal(keepAlive ? 1 : 0, await TestUtils.GetActivationCount(this.testCluster.GrainFactory!, fullGrainTypeName));
        }

        [TestSuite("SlowBVT")]
        [TestProvider("None")]
        [Fact, TestCategory("SlowBVT"), TestCategory("Timers")]
        public async Task NonReentrantGrainTimer_NoKeepAlive_Test()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, DEFAULT_COLLECTION_QUANTUM, cancellationToken);

            const string testName = "NonReentrantGrainTimer_NoKeepAlive_Test";

            var grain = this.testCluster.GrainFactory!.GetGrain<INonReentrantTimerCallGrain>(GetRandomGrainId());
            var fullGrainTypeName = RuntimeTypeNameFormatter.Format(typeof(NonReentrantTimerCallGrain));

            // 安排一个计时器在第 30 秒触发，该计时器不会延长 Grain 的生存期。
            await grain.StartTimer(testName, TimeSpan.FromSeconds(30), keepAlive: false);
            await WaitForActivationCountToConverge(fullGrainTypeName, expectedCount: 0, cancellationToken);

            var tickCount = await grain.GetTickCount();

            // Grain 应已停用。
            Assert.Equal(0, tickCount);
        }

        [TestSuite("Functional")]
        [TestProvider("None")]
        [Fact, TestCategory("Functional"), TestCategory("Diagnostics"), TestCategory("Timers")]
        public async Task GrainAndTimerDiagnosticsExposeRuntimeInstances()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await Initialize(DEFAULT_IDLE_TIMEOUT, cancellationToken);

            using var grainObserver = GrainDiagnosticObserver.Create(testCluster);
            using var timerObserver = TimerDiagnosticObserver.Create(testCluster);

            const string timerName = nameof(GrainAndTimerDiagnosticsExposeRuntimeInstances);
            var grain = this.testCluster.GrainFactory!.GetGrain<INonReentrantTimerCallGrain>(GetRandomGrainId());

            await grain.StartTimer(timerName, TimeSpan.FromMilliseconds(100), keepAlive: true);

            var created = await grainObserver.WaitForCreatedAsync(grain);
            var activated = await grainObserver.WaitForActivatedAsync(grain);
            var timerCreated = await timerObserver.WaitForTimerCreatedAsync(grain);
            var tickStop = await timerObserver.WaitForTimerTickAsync(grain);

            await grain.StopTimer(timerName);

            var disposed = await timerObserver.WaitForTimerDisposedAsync(grain);

            Assert.Equal(created.GrainContext.GrainId, grain.GetGrainId());
            Assert.Equal(activated.GrainContext.GrainId, created.GrainContext.GrainId);
            Assert.Equal(activated.GrainContext.ActivationId, created.GrainContext.ActivationId);

            Assert.Equal(timerCreated.GrainContext.GrainId, created.GrainContext.GrainId);
            Assert.NotNull(timerCreated.Timer);
            Assert.Equal(tickStop.GrainContext.GrainId, created.GrainContext.GrainId);
            Assert.NotNull(tickStop.Timer);
            Assert.Equal(disposed.GrainContext.GrainId, created.GrainContext.GrainId);
            Assert.NotNull(disposed.Timer);
        }

    }
}
