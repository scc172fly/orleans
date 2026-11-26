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

    //------------------------ One to One ----------------------//
    public async Task StreamTest_01_OneProducerGrainOneConsumerGrain(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_01_ConsumerJoinsFirstProducerLater");
        Guid streamId = Guid.NewGuid();
        // 消费者首先加入,然后生产者加入
        this.consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, this.streamProviderName, this.logger, this.client, cancellationToken: cancellationToken);
        this.producer = await ProducerProxy.NewProducerGrainsAsync(streamId, this.streamProviderName, null, this.logger, this.client, cancellationToken: cancellationToken);
        await BasicTestAsync(cancellationToken: cancellationToken);
        await StopProxies();

        streamId = Guid.NewGuid();
        // 产品先加入,后消费
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

    //和很多不同的人 grains ----------------------//

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

    //和... 和很多相同 grains ----------------------//

    public async Task StreamTest_09_ManySame_ManyProducerGrainsManyConsumerGrains(CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_09_ManySame_ManyProducerGrainsManyConsumerGrains");
        Guid streamId = Guid.NewGuid();
        Guid grain1 = Guid.NewGuid();
        Guid grain2 = Guid.NewGuid();
        Guid[] consumerGrainIds = new Guid[] { grain1, grain1, grain1 };
        Guid[] producerGrainIds = new Guid[] { grain2, grain2, grain2 };
        // 生产者先加入,消费者晚加入
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
        // 消费者首先加入,然后生产者加入
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

    //------------------------ MANY to Many producer consumer same grain ----------------------//

    public async Task StreamTest_13_SameGrain_ConsumerFirstProducerLater(
        bool useReentrantGrain,
        CancellationToken cancellationToken = default)
    {
        Heading("StreamTest_13_SameGrain_ConsumerFirstProducerLater");
        Guid streamId = Guid.NewGuid();
        int grain1 = Random.Shared.Next();
        int[] grainIds = new int[] { grain1 };
        // 消费者首先加入,然后生产者加入
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
        // 产品先加入,后消费
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
        // 光环小组发现一个方案没有(结束)工作。
        // 生产者呼叫消费者,而消费者则向该用户认购。

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

        // 消费者首先加入,然后生产者加入
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

    //公用自动同步任务 StreamTest_17_Persistence_OneProducerGrainOneConsumerGrain()
    //{
    //    Heading("StreamTest_17_Persistence_OneProducerGrainOneConsumerGrain");
    //    StreamId streamId = StreamId.NewRandomStreamId();
    //    消费者首先加入,然后生产者加入
    //    =等待 ConsumerProxy.NewConsumerGrainsAsync(streamId、 streamProviderName、登录);
    //    生产厂商 = 等待 ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName,登录器);
    //    await BasicTestAsync(false);

    //    await consumer.DeactivateOnIdle();
    //    await producer.DeactivateOnIdle();

    //    await UnitTestBase.WaitUntilAsync(() => CheckGrainsDeactivated(null, consumer, assertAreEqual: false), _timeout);
    //    await UnitTestBase.WaitUntilAsync(() => CheckGrainsDeactivated(producer, null, assertAreEqual: false), _timeout);

    //    logger.Info("*******************************************************************");
    //    等待 BasicTestAsync(虚假);
    //    等待 StopProxies();
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
        //ToDo 在移徙中:下列消费者 grain 没有在VSO中实施,所有测试都取决于其失败。
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

        // 消费者获得 silo 项的编制
        IStreamingImmutabilityTestGrain? consumerSameSilo = null;
        do
        {
            var itemConsumer = this.client.GetGrain<IStreamingImmutabilityTestGrain>(Guid.NewGuid());
            var consumerSilo = await itemConsumer.GetSiloIdentifier();

            if (consumerSilo == producerSilo)
                consumerSameSilo = itemConsumer;
        } while (consumerSameSilo == null);

        // 如果启用不可变化功能, 测试行为
        await consumerSameSilo.SubscribeToStream(itemProducer.GetPrimaryKey(), MEMORY_STREAM_PROVIDER_NAME);

        await itemProducer.SetTestObjectStringProperty("VALUE_IN_IMMUTABLE_STREAM");
        await itemProducer.SendTestObject(MEMORY_STREAM_PROVIDER_NAME);

        Assert.Equal("VALUE_IN_IMMUTABLE_STREAM", await consumerSameSilo.GetTestObjectStringProperty());

        // 现在,通过更新消费者的财产,违反了不可改变性。
        await consumerSameSilo.SetTestObjectStringProperty("ILLEGAL_CHANGE");
        Assert.Equal("ILLEGAL_CHANGE", await itemProducer.GetTestObjectStringProperty());

        await consumerSameSilo.UnsubscribeFromStream();

        // 如果禁用不可变性则测试行为
        itemProducer = this.client.GetGrain<IStreamingImmutabilityTestGrain>(Guid.NewGuid());

        await consumerSameSilo.SubscribeToStream(itemProducer.GetPrimaryKey(), MEMORY_STREAM_PROVIDER_NAME_DO_NOT_OPTIMIZE_FOR_IMMUTABLE_DATA);

        await itemProducer.SetTestObjectStringProperty("VALUE_IN_MUTABLE_STREAM");
        await itemProducer.SendTestObject(MEMORY_STREAM_PROVIDER_NAME_DO_NOT_OPTIMIZE_FOR_IMMUTABLE_DATA);

        Assert.Equal("VALUE_IN_MUTABLE_STREAM", await consumerSameSilo.GetTestObjectStringProperty());

        // 修改项目属性并检查它没有影响
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
        Assert.NotEqual(0, consumerCount);  // "没有检测到消费者"
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
        // 目前,我们只支持检查制片人 是否在短讯会合时数 grain.
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
            Assert.Equal(expectActivationCount, activationCount); // String.Format("预期{0}ActivationCount ={1},{0}ActivationCount ={2}",斯特, expectActivationCount, activationCount) );
        }
        return expectActivationCount == activationCount;
    }
}

//public async Task AQ_1_ConsumerJoinsFirstProducerLater()
//{
//    logger.Info("\n\n ************************ AQ_1_ConsumerJoinsFirstProducerLater ********************************* \n\n");
//    streamId = StreamId.NewRandomStreamId();
//    streamProviderName = AQ_STREAM_PROVIDER_NAME;
//    消费者首先加入,然后生产者加入
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//public async Task AQ_2_ProducerJoinsFirstConsumerLater()
//{
//    logger.Info("\n\n ************************ AQ_2_ProducerJoinsFirstConsumerLater ********************************* \n\n");
//    streamId = StreamId.NewRandomStreamId();
//    streamProviderName = AQ_STREAM_PROVIDER_NAME;
//    产品加入第一,消费者加入后
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[事实, TestCategory("BVT"), TestCategory(“Streaming')]
//公用自动同步任务 StreamTest_2_ProducerJoinsFirstConsumerLater()
//{
//    logger.Info("\n\n ************************ StreamTest_2_ProducerJoinsFirstConsumerLater ********************************* \n\n");
//    streamId = Guid.NewGuid();
//    streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    产品加入第一,消费者加入后
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}



//[事实, TestCategory("BVT"), TestCategory(“Streaming')]
//公用自动同步任务 StreamTestProducerOnly()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    await TestGrainProducerOnlyAsync(streamId, this.streamProviderName);
//}

//[事实, TestCategory("BVT"), TestCategory(“Streaming')]
//公共无效 AQProducerOnly()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = AQ_STREAM_PROVIDER_NAME;
//    TestGrainProducerOnlyAsync(streamId, this.streamProviderName).等待();
//}

//私有的自动同步任务 TestGrainProducerOnlyAsync(指南 streamId,字符串 streamProvider)
//{
//    没有消费者,只有一个生产者
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProvider, logger);

//    await producer.ProduceSequentialSeries(0, ItemsPerSeries);
//    var numProduced = await producer.NumberProduced;
//    logger.Info("numProduced = " + numProduced);
//    Assert.Equal(numProduced, ItemsPerSeries);

//    / 注意到连续呼叫Do...Production()的方法返回的数值是一个累计总计。
//    await producer.ProduceParallelSeries(ItemsPerSeries, ItemsPerSeries);
//    numProduced =等待 producer.NumberProduced;
//    logger.Info("numProduced = " + numProduced);
//    Assert.Equal(numProduced, ItemsPerSeries * 2);
//}


////------------------------ MANY to One ----------------------//

//[事实, TestCategory("BVT"), TestCategory(“Streaming')]
//公用自动同步任务 StreamTest_Many_5_ManyProducerGrainsOneConsumerGrain()
//{
//    logger.Info("\n\n ************************ StreamTest_6_ManyProducerGrainsOneConsumerGrain ********************************* \n\n");
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//[事实, TestCategory("BVT"), TestCategory(“Streaming')]
//公用自动同步任务 StreamTest_Many6_OneProducerGrainManyConsumerGrains()
//{
//    logger.Info("\n\n ************************ StreamTest_7_OneProducerGrainManyConsumerGrains ********************************* \n\n");
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[事实, TestCategory("稳定" ]
//公用自动同步任务 StreamTest_Many_8_ManyProducerGrainsOneConsumerClient()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//// 注意:由于 StreamTest 提供者的同步问题,目前这项测试间歇性失败。 
//// 将夜间建筑从夜间建筑中移除,直到这个问题得到解决。
//[事实, TestCategory("稳定" ]
//公共 async 任务 _ StreamTestManY 制作器 Clients OneConsummerGrain ()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//// 注意:由于 StreamTest 提供者的同步问题,目前这项测试间歇性失败。 
//// 将夜间建筑从夜间建筑中移除,直到这个问题得到解决。
//[事实, TestCategory("稳定" ]
//公共用户 Async 任务_ StreamTestMenyPunderClients One ConsumerClient ()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    await BasicTestAsync(producer, consumer);
//}

//[事实, TestCategory("稳定" ]
//公共辅助任务 _StreamTest 一名生产者 GrainManyConsumer Clients ()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerGrainsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[事实, TestCategory("稳定" ]
//公共辅助工作 _StreamTesto 一名制片人 Client MnyCummerGrains ()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerGrainsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}

//[事实, TestCategory("稳定" ]
//公共辅助任务 _StreamTests 一名制作者 Client MnyCummerClient ()
//{
//    streamId = Guid.NewGuid();
//    this.streamProviderName = StreamTest_STREAM_PROVIDER_NAME;
//    var consumer = await ConsumerProxy.NewConsumerClientObjectsAsync(streamId, streamProviderName, logger, Many);
//    var producer = await ProducerProxy.NewProducerClientObjectsAsync(streamId, streamProviderName, logger);
//    await BasicTestAsync(producer, consumer);
//}
