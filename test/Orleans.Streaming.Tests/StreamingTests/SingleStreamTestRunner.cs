#nullable enable
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using TestExtensions;
using Tester;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace UnitTests.StreamingTests;

[TestSuite("Functional")]
[TestArea("Streaming")]
public class SingleStreamTestRunner
{
    public const string MEMORY_STREAM_PROVIDER_NAME = StreamTestsConstants.MEMORY_STREAM_PROVIDER_NAME;
    public const string MEMORY_STREAM_PROVIDER_NAME_DO_NOT_OPTIMIZE_FOR_IMMUTABLE_DATA = "MemoryStreamProviderDoNotOptimizeForImmutableData";
    public const string AQ_STREAM_PROVIDER_NAME = StreamTestsConstants.AZURE_QUEUE_STREAM_PROVIDER_NAME;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private ProducerProxy producer = null!;
    private ConsumerProxy consumer = null!;
    private const int Many = 3;
    private const int ItemCount = 10;
    private readonly ILogger logger;
    private readonly string streamProviderName;
    private readonly int testNumber;
    private readonly bool runFullTest;
    private readonly IInternalClusterClient client;
    private readonly Func<StreamingDiagnosticObserver> createObserver;

    internal SingleStreamTestRunner(TestCluster testCluster, string streamProvider, int testNum = 0, bool fullTest = true)
        : this(testCluster.InternalClient!, () => StreamingDiagnosticObserver.Create(testCluster), streamProvider, testNum, fullTest)
    {
    }

    internal SingleStreamTestRunner(InProcessTestCluster testCluster, string streamProvider, int testNum = 0, bool fullTest = true)
        : this(testCluster.InternalClient!, () => StreamingDiagnosticObserver.Create(testCluster), streamProvider, testNum, fullTest)
    {
    }

    private SingleStreamTestRunner(
        IInternalClusterClient client,
        Func<StreamingDiagnosticObserver> createObserver,
        string streamProvider,
        int testNum,
        bool fullTest)
    {
        this.client = client;
        this.createObserver = createObserver;
        this.streamProviderName = streamProvider;
        this.logger = TestingUtils.CreateDefaultLoggerFactory($"{this.GetType().Name}.log").CreateLogger<SingleStreamTestRunner>();
        this.testNumber = testNum;
        this.runFullTest = fullTest;
    }

    private void Heading(string testName)
    {
        logger.LogInformation("\n\n************************ {TestNumber} {StreamProviderName}_{TestName} ********************************* \n\n", testNumber, streamProviderName, testName);
    }

    //------------------------ 一对一 ----------------------//
    public async Task StreamTest_01_OneProducerGrainOneConsumerGrain(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_01_ConsumerJoinsFirstProducerLater");
        Guid streamId = Guid.NewGuid();
        // 消费者先加入，生产者随后加入。
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();

        streamId = Guid.NewGuid();
        // 生产者先加入，消费者后加入。
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_02_OneProducerGrainOneConsumerClient(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_02_OneProducerGrainOneConsumerClient");
        Guid streamId = Guid.NewGuid();
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();

        streamId = Guid.NewGuid();
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_03_OneProducerClientOneConsumerGrain(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_03_OneProducerClientOneConsumerGrain");
        Guid streamId = Guid.NewGuid();
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, cancellationToken: cancellationToken);
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();

        streamId = Guid.NewGuid();
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, cancellationToken: cancellationToken);
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_04_OneProducerClientOneConsumerClient(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_04_OneProducerClientOneConsumerClient");
        Guid streamId = Guid.NewGuid();
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, cancellationToken: cancellationToken);
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();

        streamId = Guid.NewGuid();
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, cancellationToken: cancellationToken);
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    // ------------------------ 不同 Grain 的多对多测试 ----------------------//

    public async Task StreamTest_05_ManyDifferent_ManyProducerGrainsManyConsumerGrains(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_05_ManyDifferent_ManyProducerGrainsManyConsumerGrains");
        Guid streamId = Guid.NewGuid();
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, null, Many, cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, null, Many, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_06_ManyDifferent_ManyProducerGrainManyConsumerClients(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_06_ManyDifferent_ManyProducerGrainManyConsumerClients");
        Guid streamId = Guid.NewGuid();
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, Many, cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, null, Many, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_07_ManyDifferent_ManyProducerClientsManyConsumerGrains(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_07_ManyDifferent_ManyProducerClientsManyConsumerGrains");
        Guid streamId = Guid.NewGuid();
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, null, Many, cancellationToken);
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, Many, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_08_ManyDifferent_ManyProducerClientsManyConsumerClients(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_08_ManyDifferent_ManyProducerClientsManyConsumerClients");
        Guid streamId = Guid.NewGuid();
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, Many, cancellationToken);
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, Many, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    // ------------------------ 相同 Grain 的多对多测试 ----------------------//

    public async Task StreamTest_09_ManySame_ManyProducerGrainsManyConsumerGrains(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_09_ManySame_ManyProducerGrainsManyConsumerGrains");
        Guid streamId = Guid.NewGuid();
        Guid grain1 = Guid.NewGuid();
        Guid grain2 = Guid.NewGuid();
        Guid[] consumerGrainIds = new Guid[] { grain1, grain1, grain1 };
        Guid[] producerGrainIds = new Guid[] { grain2, grain2, grain2 };
        // 生产者先加入，消费者随后加入。
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, producerGrainIds, cancellationToken: cancellationToken);
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, consumerGrainIds, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_10_ManySame_ManyConsumerGrainsManyProducerGrains(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_10_ManySame_ManyConsumerGrainsManyProducerGrains");
        Guid streamId = Guid.NewGuid();
        Guid grain1 = Guid.NewGuid();
        Guid grain2 = Guid.NewGuid();
        Guid[] consumerGrainIds = new Guid[] { grain1, grain1, grain1 };
        Guid[] producerGrainIds = new Guid[] { grain2, grain2, grain2 };
        // 消费者先加入，生产者随后加入。
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, consumerGrainIds, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, producerGrainIds, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_11_ManySame_ManyProducerGrainsManyConsumerClients(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_11_ManySame_ManyProducerGrainsManyConsumerClients");
        Guid streamId = Guid.NewGuid();
        Guid grain1 = Guid.NewGuid();
        Guid[] producerGrainIds = new Guid[] { grain1, grain1, grain1, grain1 };
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, producerGrainIds, cancellationToken: cancellationToken);
        consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, this.client, Many, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_12_ManySame_ManyProducerClientsManyConsumerGrains(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_12_ManySame_ManyProducerClientsManyConsumerGrains");
        Guid streamId = Guid.NewGuid();
        Guid grain1 = Guid.NewGuid();
        Guid[] consumerGrainIds = new Guid[] { grain1, grain1, grain1 };
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, consumerGrainIds, cancellationToken: cancellationToken);
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, null, logger, this.client, Many, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    // ------------------------ 同一 Grain 中的多生产者、多消费者 ----------------------//

    public async Task StreamTest_13_SameGrain_ConsumerFirstProducerLater(
        bool useReentrantGrain,
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_13_SameGrain_ConsumerFirstProducerLater");
        Guid streamId = Guid.NewGuid();
        int grain1 = Random.Shared.Next();
        int[] grainIds = new int[] { grain1 };
        // 消费者先加入，生产者随后加入。
        this.consumer = await ConsumerProxy.NewProducerConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, grainIds, useReentrantGrain, this.client, cancellationToken);
        this.producer = await ProducerProxy.NewProducerConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, grainIds, useReentrantGrain, this.client, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    public async Task StreamTest_14_SameGrain_ProducerFirstConsumerLater(
        bool useReentrantGrain,
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_14_SameGrain_ProducerFirstConsumerLater");
        Guid streamId = Guid.NewGuid();
        int grain1 = Random.Shared.Next();
        int[] grainIds = new int[] { grain1 };
        // 生产者先加入，消费者后加入。
        this.producer = await ProducerProxy.NewProducerConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, grainIds, useReentrantGrain, this.client, cancellationToken);
        this.consumer = await ConsumerProxy.NewProducerConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, grainIds, useReentrantGrain, this.client, cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    //----------------------------------------------//

    public async Task StreamTest_15_ConsumeAtProducersRequest(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_15_ConsumeAtProducersRequest");
        Guid streamId = Guid.NewGuid();
        // 此测试重现 Halo 团队发现的一个无法正常运行（发生死锁）的场景，即
        // 生产者调用消费者，而该消费者又订阅发起调用的生产者。

        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
        Guid consumerGrainId = await producer.AddNewConsumerGrain();

        this.consumer = ConsumerProxy.NewConsumerGrainAsync_WithoutBecomeConsumer(consumerGrainId, this.logger, this.client);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();
    }

    internal async Task StreamTest_Create_OneProducerGrainOneConsumerGrain(
        CancellationToken cancellationToken = default)
    {
        Guid streamId = Guid.NewGuid();
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
    }

    private async Task ProducePhaseAndWaitForDeliveriesAsync(
        Func<Task> produce,
        CancellationToken cancellationToken)
    {
        var consumerCount = await consumer.ConsumerCount;
        Assert.NotEqual(0, consumerCount);

        var producedBefore = await producer.ExpectedItemsProduced;
        using var observer = createObserver();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        await produce();

        var producedAfter = await producer.ExpectedItemsProduced;
        var producedThisPhase = producedAfter - producedBefore;
        Assert.True(producedThisPhase > 0, "No items were produced during the current phase.");

        await observer.WaitForItemDeliveryCountAsync(producer.StreamId, producedThisPhase * consumerCount, producer.ProviderName, cts.Token);
    }

    private async Task ProduceAndWaitForImplicitDeliveriesAsync(
        int count,
        CancellationToken cancellationToken)
    {
        using var observer = createObserver();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        await producer.ProduceSequentialSeries(count);
        await observer.WaitForItemDeliveryCountAsync(producer.StreamId, count, producer.ProviderName, cts.Token);
    }

    public async Task StreamTest_16_Deactivation_OneProducerGrainOneConsumerGrain(
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_16_Deactivation_OneProducerGrainOneConsumerGrain");
        Guid streamId = Guid.NewGuid();
        Guid[] consumerGrainIds = { Guid.NewGuid() };
        Guid[] producerGrainIds = { Guid.NewGuid() };

        // 消费者先加入，生产者随后加入。
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, consumerGrainIds, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, producerGrainIds, cancellationToken: cancellationToken);
        await BasicTestAsync(false, cancellationToken);
        //await consumer.StopBeingConsumer();
        await StopProxies();

        await consumer.DeactivateOnIdle();
        await producer.DeactivateOnIdle();

        await TestingUtils.WaitUntilAsync(
            (lastTry, token) => CheckGrainsDeactivated(null, consumer, lastTry, token),
            _timeout,
            delayOnFail: TimeSpan.FromMilliseconds(100),
            cancellationToken: cancellationToken);
        await TestingUtils.WaitUntilAsync(
            (lastTry, token) => CheckGrainsDeactivated(producer, null, lastTry, token),
            _timeout,
            delayOnFail: TimeSpan.FromMilliseconds(100),
            cancellationToken: cancellationToken);

        logger.LogInformation("\n\n\n*******************************************************************\n\n\n");

        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, consumerGrainIds, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, producerGrainIds, cancellationToken: cancellationToken);

        await BasicTestAsync(false, cancellationToken);
        await StopProxies();
    }

    //public async Task StreamTest_17_Persistence_OneProducerGrainOneConsumerGrain()
    //{
    //    Heading("StreamTest_17_Persistence_OneProducerGrainOneConsumerGrain");
    //    StreamId streamId = StreamId.NewRandomStreamId();
    //    // 消费者先加入，生产者后加入。
    //    consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
    //    producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
    //    await BasicTestAsync(false);

    //    await consumer.DeactivateOnIdle();
    //    await producer.DeactivateOnIdle();

    //    await UnitTestBase.WaitUntilAsync(() => CheckGrainsDeactivated(null, consumer, assertAreEqual: false), _timeout);
    //    await UnitTestBase.WaitUntilAsync(() => CheckGrainsDeactivated(producer, null, assertAreEqual: false), _timeout);

    //    logger.Info("*******************************************************************");
    //    //await BasicTestAsync(false);
    //    //await StopProxies();
    //}

    public async Task StreamTest_19_ConsumerImplicitlySubscribedToProducerClient(
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_19_ConsumerImplicitlySubscribedToProducerClient");
        string consumerTypeName = typeof(Streaming_ImplicitlySubscribedConsumerGrain).FullName!;
        Guid streamGuid = Guid.NewGuid();
        producer = await ProducerProxy.NewProducerClientObjectsAsync(streamGuid, streamProviderName, "TestNamespace1", logger, this.client, cancellationToken: cancellationToken);
        this.consumer = ConsumerProxy.NewConsumerGrainAsync_WithoutBecomeConsumer(streamGuid, this.logger, this.client, consumerTypeName);

        logger.LogInformation("\n** Starting Test {TestNumber}.\n", testNumber);
        var producerCount = await producer.ProducerCount;
        logger.LogInformation("\n** Test {TestNumber} BasicTestAsync: producerCount={ProducerCount}.\n", testNumber, producerCount);

        _ = consumerTypeName;
        await ProduceAndWaitForImplicitDeliveriesAsync(ItemCount, cancellationToken);
        await CheckCounters(producer, consumer);
        await StopProxies();
    }

    public async Task StreamTest_20_ConsumerImplicitlySubscribedToProducerGrain(
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_20_ConsumerImplicitlySubscribedToProducerGrain");
        string consumerTypeName = typeof(Streaming_ImplicitlySubscribedConsumerGrain).FullName!;
        Guid streamGuid = Guid.NewGuid();
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamGuid, this.streamProviderName, "TestNamespace1", this.logger, this.client, cancellationToken: cancellationToken);
        this.consumer = ConsumerProxy.NewConsumerGrainAsync_WithoutBecomeConsumer(streamGuid, this.logger, this.client, consumerTypeName);

        logger.LogInformation("\n** Starting Test {TestNumber}.\n", testNumber);
        var producerCount = await producer.ProducerCount;
        logger.LogInformation("\n** Test {TestNumber} BasicTestAsync: producerCount={ProducerCount}.\n", testNumber, producerCount);

        _ = consumerTypeName;
        await ProduceAndWaitForImplicitDeliveriesAsync(ItemCount, cancellationToken);
        await CheckCounters(producer, consumer);
        await StopProxies();
    }

    public async Task StreamTest_21_GenericConsumerImplicitlySubscribedToProducerGrain(
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_21_GenericConsumerImplicitlySubscribedToProducerGrain");
        //迁移待办事项：以下消费者 Grain 在 VSO 中尚未实现，所有依赖它的测试均会失败。
        string consumerTypeName = "UnitTests.Grains.Streaming_ImplicitlySubscribedGenericConsumerGrain";//typeof(Streaming_ImplicitlySubscribedGenericConsumerGrain).FullName;
        Guid streamGuid = Guid.NewGuid();
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamGuid, this.streamProviderName, "TestNamespace1", this.logger, this.client, cancellationToken: cancellationToken);
        this.consumer = ConsumerProxy.NewConsumerGrainAsync_WithoutBecomeConsumer(streamGuid, this.logger, this.client, consumerTypeName);

        logger.LogInformation("\n** Starting Test {TestNumber}.\n", testNumber);
        var producerCount = await producer.ProducerCount;
        logger.LogInformation("\n** Test {TestNumber} BasicTestAsync: producerCount={ProducerCount}.\n", testNumber, producerCount);

        _ = consumerTypeName;
        await ProduceAndWaitForImplicitDeliveriesAsync(ItemCount, cancellationToken);
        await CheckCounters(producer, consumer);
        await StopProxies();
    }

    public async Task StreamTest_22_TestImmutabilityDuringStreaming()
    {
        Heading("StreamTest_22_TestImmutabilityDuringStreaming");

        IStreamingImmutabilityTestGrain itemProducer = this.client.GetGrain<IStreamingImmutabilityTestGrain>(Guid.NewGuid());
        string producerSilo = await itemProducer.GetSiloIdentifier();

        // 获取与条目生产者位于同一个 Silo 的消费者。
        IStreamingImmutabilityTestGrain? consumerSameSilo = null;
        do
        {
            var itemConsumer = this.client.GetGrain<IStreamingImmutabilityTestGrain>(Guid.NewGuid());
            var consumerSilo = await itemConsumer.GetSiloIdentifier();

            if (consumerSilo == producerSilo)
                consumerSameSilo = itemConsumer;
        } while (consumerSameSilo == null);

        // 测试启用不可变性时的行为。
        await consumerSameSilo.SubscribeToStream(itemProducer.GetPrimaryKey(), MEMORY_STREAM_PROVIDER_NAME);

        await itemProducer.SetTestObjectStringProperty("VALUE_IN_IMMUTABLE_STREAM");
        await itemProducer.SendTestObject(MEMORY_STREAM_PROVIDER_NAME);

        Assert.Equal("VALUE_IN_IMMUTABLE_STREAM", await consumerSameSilo.GetTestObjectStringProperty());

        // 现在通过更新消费者中的属性来违反不可变性约束。
        await consumerSameSilo.SetTestObjectStringProperty("ILLEGAL_CHANGE");
        Assert.Equal("ILLEGAL_CHANGE", await itemProducer.GetTestObjectStringProperty());

        await consumerSameSilo.UnsubscribeFromStream();

        // 测试禁用不可变性时的行为。
        itemProducer = this.client.GetGrain<IStreamingImmutabilityTestGrain>(Guid.NewGuid());

        await consumerSameSilo.SubscribeToStream(itemProducer.GetPrimaryKey(), MEMORY_STREAM_PROVIDER_NAME_DO_NOT_OPTIMIZE_FOR_IMMUTABLE_DATA);

        await itemProducer.SetTestObjectStringProperty("VALUE_IN_MUTABLE_STREAM");
        await itemProducer.SendTestObject(MEMORY_STREAM_PROVIDER_NAME_DO_NOT_OPTIMIZE_FOR_IMMUTABLE_DATA);

        Assert.Equal("VALUE_IN_MUTABLE_STREAM", await consumerSameSilo.GetTestObjectStringProperty());

        // 修改条目的属性，并检查该修改没有影响。
        await consumerSameSilo.SetTestObjectStringProperty("ALLOWED_CHANGE");
        Assert.Equal("ALLOWED_CHANGE", await consumerSameSilo.GetTestObjectStringProperty());
        Assert.Equal("VALUE_IN_MUTABLE_STREAM", await itemProducer.GetTestObjectStringProperty());

        await consumerSameSilo.UnsubscribeFromStream();
    }

    //-----------------------------------------------------------------------------//

    public async Task BasicTestAsync(
        bool fullTest = true,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("\n** Starting Test {TestNumber} BasicTestAsync.\n", testNumber);
        var producerCount = await producer.ProducerCount;
        var consumerCount = await consumer.ConsumerCount;
        logger.LogInformation(
            "\n** Test {TestNumber} BasicTestAsync: producerCount={ProducerCount}, consumerCount={ConsumerCount}.\n",
            testNumber,
            producerCount,
            consumerCount);

        await ProducePhaseAndWaitForDeliveriesAsync(() => producer.ProduceSequentialSeries(ItemCount), cancellationToken);
        await CheckCounters(producer, consumer);
        if (runFullTest)
        {
            await ProducePhaseAndWaitForDeliveriesAsync(() => producer.ProduceParallelSeries(ItemCount), cancellationToken);
            await CheckCounters(producer, consumer);

            await ProducePhaseAndWaitForDeliveriesAsync(() => producer.ProducePeriodicSeries(ItemCount), cancellationToken);
            await CheckCounters(producer, consumer);
        }
        await ValidatePubSub(producer.StreamId, producer.ProviderName);
    }

    public async Task StopProxies()
    {
        await producer.StopBeingProducer();
        await AssertProducerCount(0, producer.ProviderName, producer.StreamIdGuid);
        await consumer.StopBeingConsumer();
    }

    private async Task<bool> CheckCounters(ProducerProxy producer, ConsumerProxy consumer, bool assertAreEqual = true)
    {
        var consumerCount = await consumer.ConsumerCount;
        Assert.NotEqual(0, consumerCount);  // 未检测到消费者。
        _ = await producer.ProducerCount;
        var numProduced = await producer.ExpectedItemsProduced;
        var expectConsumed = numProduced * consumerCount;
        var numConsumed = await consumer.ItemsConsumed;
        logger.LogInformation(
            "Test {TestNumber} CheckCounters: numProduced = {NumProduced}, expectConsumed = {ExpectedConsumed}, numConsumed = {NumConsumed}",
            testNumber,
            numProduced,
            expectConsumed,
            numConsumed);
        if (assertAreEqual)
        {
            Assert.Equal(expectConsumed, numConsumed); // String.Format("expectConsumed = {0}, numConsumed = {1}", expectConsumed, numConsumed));
            return true;
        }
        else
        {
            return expectConsumed == numConsumed;
        }
    }

    private async Task AssertProducerCount(int expectedCount, string providerName, Guid streamIdGuid)
    {
        // 目前仅支持检查 SMS 汇合 Grain 上的生产者数量。
        if (providerName == MEMORY_STREAM_PROVIDER_NAME)
        {
            var streamId = StreamId.Create(StreamTestsConstants.DefaultStreamNamespace, streamIdGuid);
            var actualCount = await StreamTestUtils.GetStreamPubSub(this.client).ProducerCount(new QualifiedStreamId(providerName, streamId));
            logger.LogInformation(
                "StreamingTestRunner.AssertProducerCount: expected={ExpectedCount} actual (SMSStreamRendezvousGrain.ProducerCount)={ActualCount} streamId={StreamId}",
                expectedCount,
                actualCount,
                streamId);
            Assert.Equal(expectedCount, actualCount);
        }
    }

    private Task ValidatePubSub(StreamId streamId, string providerName)
    {
        var intStreamId = new QualifiedStreamId(providerName, streamId);
        var rendez = this.client.GetGrain<IPubSubRendezvousGrain>(intStreamId.ToString());
        return rendez.Validate();
    }

    private async Task<bool> CheckGrainsDeactivated(ProducerProxy? producer, ConsumerProxy? consumer, bool assertAreEqual, CancellationToken cancellationToken)
    {
        var activationCount = 0;
        string str = "";
        if (producer != null)
        {
            str = "Producer";
            activationCount = await producer.GetNumActivations(this.client, cancellationToken);
        }
        else if (consumer != null)
        {
            str = "Consumer";
            activationCount = await consumer.GetNumActivations(this.client, cancellationToken);
        }
        var expectActivationCount = 0;
        logger.LogInformation(
            "Test {TestNumber} CheckGrainsDeactivated: {Type}ActivationCount = {ActivationCount}, Expected{Type}ActivationCount = {ExpectActivationCount}",
            testNumber,
            str,
            activationCount,
            str,
            expectActivationCount);
        if (assertAreEqual)
        {
            Assert.Equal(expectActivationCount, activationCount); // String.Format("Expected{0}ActivationCount = {1}, {0}ActivationCount = {2}", str, expectActivationCount, activationCount));
        }
        return expectActivationCount == activationCount;
    }
}

//public async Task AQ_1_ConsumerJoinsFirstProducerLater()
//{
//    logger.Info("\n\n ************************ AQ_1_ConsumerJoinsFirstProducerLater ********************************* \n\n");
//    streamId = StreamId.NewRandomStreamId();
//    streamProviderName = AQ_STREAM_PROVIDER_NAME;
//    // 消费者先加入，生产者后加入。
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//public async Task AQ_2_ProducerJoinsFirstConsumerLater()
//{
//    logger.Info("\n\n ************************ AQ_2_ProducerJoinsFirstConsumerLater ********************************* \n\n");
//    streamId = StreamId.NewRandomStreamId();
//    streamProviderName = AQ_STREAM_PROVIDER_NAME;
//    // 生产者先加入，消费者后加入。
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[Fact, TestCategory("BVT"), TestCategory("Streaming")]
//public async Task StreamTest_2_ProducerJoinsFirstConsumerLater()
//{
//    logger.Info("\n\n ************************ StreamTest_2_ProducerJoinsFirstConsumerLater ********************************* \n\n");
//    streamId = Guid.NewGuid();
//    streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    // 生产者先加入，消费者后加入。
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}



//[Fact, TestCategory("BVT"), TestCategory("Streaming")]
//public async Task StreamTestProducerOnly()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    await TestGrainProducerOnlyAsync(streamId, this.streamProviderName);
//}

//[Fact, TestCategory("BVT"), TestCategory("Streaming")]
//public void AQProducerOnly()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = AQ_STREAM_PROVIDER_NAME;
//    TestGrainProducerOnlyAsync(streamId, this.streamProviderName).Wait();
//}

//private async Task TestGrainProducerOnlyAsync(Guid streamId, string streamProvider)
//{
//    // 没有消费者，只有一个生产者。
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProvider, logger);

//    await producer.ProduceSequentialSeries(0, ItemsPerSeries);
//    var numProduced = await producer.NumberProduced;
//    logger.Info("numProduced = " + numProduced);
//    Assert.Equal(numProduced, ItemsPerSeries);

//    // 注意：连续调用 Do...Production() 方法返回的值是累计总数。
//    await producer.ProduceParallelSeries(ItemsPerSeries, ItemsPerSeries);
//    numProduced = await producer.NumberProduced;
//    logger.Info("numProduced = " + numProduced);
//    Assert.Equal(numProduced, ItemsPerSeries * 2);
//}


////------------------------ 多对一 ----------------------//

//[Fact, TestCategory("BVT"), TestCategory("Streaming")]
//public async Task StreamTest_Many_5_ManyProducerGrainsOneConsumerGrain()
//{
//    logger.Info("\n\n ************************ StreamTest_6_ManyProducerGrainsOneConsumerGrain ********************************* \n\n");
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//[Fact, TestCategory("BVT"), TestCategory("Streaming")]
//public async Task StreamTest_Many6_OneProducerGrainManyConsumerGrains()
//{
//    logger.Info("\n\n ************************ StreamTest_7_OneProducerGrainManyConsumerGrains ********************************* \n\n");
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[Fact, TestCategory("Streaming")]
//public async Task StreamTest_Many_8_ManyProducerGrainsOneConsumerClient()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//// 注意：此测试目前因 StreamTest 提供程序中的同步问题而间歇性失败，在解决该问题之前，
//// 已将其从每晚构建中移除。
//[Fact, TestCategory("Streaming")]
//public async Task _StreamTestManyProducerClientsOneConsumerGrain()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//// 注意：此测试目前因 StreamTest 提供程序中的同步问题而间歇性失败，在解决该问题之前，
//// 已将其从每晚构建中移除。
//[Fact, TestCategory("Streaming")]
//public async Task _StreamTestManyProducerClientsOneConsumerClient()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//[Fact, TestCategory("Streaming")]
//public async Task _StreamTestOneProducerGrainManyConsumerClients()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[Fact, TestCategory("Streaming")]
//public async Task _StreamTestOneProducerClientManyConsumerGrains()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[Fact, TestCategory("Streaming")]
//public async Task _StreamTestOneProducerClientManyConsumerClients()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}
