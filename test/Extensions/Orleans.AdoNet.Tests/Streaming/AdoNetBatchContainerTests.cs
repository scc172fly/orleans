using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streaming.AdoNet;
using TestExtensions;

namespace Tester.AdoNet.Streaming;

/// <summary>
/// <see cref="AdoNetBatchContainer"/> 的测试。
/// </summary>
[Collection(TestEnvironmentFixture.DefaultCollection)]
[TestCategory("BVT"), TestCategory("AdoNet"), TestCategory("Streaming")]
[TestSuite("Functional")]
[TestProvider("None")]
[TestArea("Streaming")]
public class AdoNetBatchContainerTests(TestEnvironmentFixture fixture)
{
    [Fact]
    public void AdoNetBatchContainer_Constructs()
    {
        // 准备
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };

        // 执行
        var container = new AdoNetBatchContainer(streamId, events, requestContext);

        // 验证
        Assert.Equal(streamId, container.StreamId);
        Assert.Equal(events, container.Events);
        Assert.Equal(requestContext, container.RequestContext);
        Assert.Null(container.SequenceToken);
        Assert.Equal(0, container.Dequeued);
    }

    [Fact]
    public void AdoNetBatchContainer_FromMessage_CreatesContainer()
    {
        // 准备
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1), new OtherModel(2), new TestModel(3), new OtherModel(4) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };
        var temp = new AdoNetBatchContainer(streamId, events, requestContext);
        var serializer = fixture.Serializer.GetSerializer<AdoNetBatchContainer>();
        var payload = serializer.SerializeToArray(temp);
        var message = new AdoNetStreamMessage("MyServiceId", "MyProviderId", "MyQueueId", 123, 234, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, payload);

        // 执行
        var container = AdoNetBatchContainer.FromMessage(serializer, message, 7);

        // 验证
        Assert.Equal(streamId, container.StreamId);
        Assert.Equal(events, container.Events);
        Assert.Equal(requestContext, container.RequestContext);
        Assert.Equal(new EventSequenceTokenV2(7), container.SequenceToken);
        Assert.Equal(123, container.MessageId);
        Assert.Equal(234, container.Dequeued);
    }

    [Fact]
    public void AdoNetBatchContainer_ToMessagePayload_CreatesPayload()
    {
        // 准备
        var serializer = fixture.Serializer.GetSerializer<AdoNetBatchContainer>();
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1), new OtherModel(2), new TestModel(3), new OtherModel(4) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };

        // 执行
        var payload = AdoNetBatchContainer.ToMessagePayload(serializer, streamId, events, requestContext);

        // 验证
        var container = serializer.Deserialize(payload);
        Assert.NotNull(container);
        Assert.Equal(streamId, container.StreamId);
        Assert.Equal(events, container.Events);
        Assert.Equal(requestContext, container.RequestContext);
        Assert.Null(container.SequenceToken);
        Assert.Equal(0, container.Dequeued);
    }

    [Fact]
    public void AdoNetBatchContainer_GetEvents_ThrowsOnHalfBaked()
    {
        // 准备
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1), new OtherModel(2), new TestModel(3), new OtherModel(4) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };

        // 执行
        var container = new AdoNetBatchContainer(streamId, events, requestContext);

        // 验证
        Assert.Throws<InvalidOperationException>(container.GetEvents<TestModel>);
    }

    [Fact]
    public void AdoNetBatchContainer_GetEvents_FiltersEvents()
    {
        // 准备
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1), new OtherModel(2), new TestModel(3), new OtherModel(4) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };
        var temp = new AdoNetBatchContainer(streamId, events, requestContext);
        var serializer = fixture.Serializer.GetSerializer<AdoNetBatchContainer>();
        var payload = serializer.SerializeToArray(temp);
        var message = new AdoNetStreamMessage("MyServiceId", "MyProviderId", "MyQueueId", 123, 234, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, payload);

        // 执行
        var container = AdoNetBatchContainer.FromMessage(serializer, message, 7);

        // 验证
        Assert.Equal([new TestModel(1), new TestModel(3)], container.GetEvents<TestModel>().Select(x => x.Item1));
        Assert.Equal([new EventSequenceTokenV2(7, 0), new EventSequenceTokenV2(7, 1)], container.GetEvents<TestModel>().Select(x => x.Item2));
        Assert.Equal([new OtherModel(2), new OtherModel(4)], container.GetEvents<OtherModel>().Select(x => x.Item1));
        Assert.Equal([new EventSequenceTokenV2(7, 0), new EventSequenceTokenV2(7, 1)], container.GetEvents<OtherModel>().Select(x => x.Item2));
    }

    [Fact]
    public void AdoNetBatchContainer_ImportsRequestContext()
    {
        // 准备
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };

        // 执行
        var container = new AdoNetBatchContainer(streamId, events, requestContext);

        // 验证
        Assert.Equal(streamId, container.StreamId);
        Assert.Equal(events, container.Events);
        Assert.Equal(requestContext, container.RequestContext);
        Assert.Null(container.SequenceToken);
        Assert.Equal(0, container.Dequeued);
    }

    [Fact]
    public void AdoNetBatchContainer_ToString_Renders()
    {
        // 准备
        var streamId = StreamId.Create("MyNamespace", "MyKey");
        var events = new List<object> { new TestModel(1) };
        var requestContext = new Dictionary<string, object> { { "MyKey", "Value" } };
        var container = new AdoNetBatchContainer(streamId, events, requestContext);

        // 执行
        var result = container.ToString();

        // 验证
        Assert.Equal($"[{nameof(AdoNetBatchContainer)}:Stream={streamId},#Items={events.Count}]", result);
    }

    [GenerateSerializer]
    [Alias("Tester.AdoNet.Streaming.AdoNetBatchContainerTests.TestModel")]
    public record TestModel(
        [property: Id(0)] int Value);

    [GenerateSerializer]
    [Alias("Tester.AdoNet.Streaming.AdoNetBatchContainerTests.OtherModel")]
    public record OtherModel(
        [property: Id(0)] int Value);
}
