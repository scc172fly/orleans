using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;
using TestExtensions;
using Xunit;

namespace DefaultCluster.Tests
{
    /// <summary>
    /// 用于通过 Orleans Grain 调用测试 JsonNode 序列化的 Grain 接口。
    /// 此接口复现 GitHub issue #9568 的场景：在 Grain 方法中使用 JsonNode 参数，
    /// 运行时可能收到 JsonValue 实例。
    /// </summary>
    public interface IJsonNodeTestGrain : IGrainWithIntegerKey
    {
        Task<JsonNode?> ProcessJsonNode(JsonNode? node);
        Task<string> GetJsonString(JsonNode? node);
    }

    /// <summary>
    /// JsonNode 测试 Grain 的实现。
    /// 提供通过 Grain 方法调用测试 JsonNode 实例序列化和反序列化的方法，
    /// 确保 issue #9568 的修复在分布式环境中有效。
    /// </summary>
    public class JsonNodeTestGrain : Grain, IJsonNodeTestGrain
    {
        public Task<JsonNode?> ProcessJsonNode(JsonNode? node)
        {
            // 直接返回节点即可，序列化机制应能处理它
            return Task.FromResult(node);
        }

        public Task<string> GetJsonString(JsonNode? node)
        {
            // 转换为字符串，以验证收到的数据正确
            return Task.FromResult(node?.ToJsonString() ?? "null");
        }
    }

    /// <summary>
    /// 配置支持 JSON 序列化的进程内 Orleans 集群的测试夹具。
    /// 将 Silo 和客户端均设置为对 System.Text.Json 类型使用 JsonCodec。
    /// </summary>
    public class JsonNodeGrainTestFixture : BaseInProcessTestClusterFixture
    {
        protected override void ConfigureTestCluster(InProcessTestClusterBuilder builder)
        {
            base.ConfigureTestCluster(builder);

            // 配置 JSON 序列化
            builder.ConfigureSilo((options, siloBuilder) =>
            {
                siloBuilder.Services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddJsonSerializer(_ => false);
                });
            });

            builder.ConfigureClient(clientBuilder =>
            {
                clientBuilder.Services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddJsonSerializer(_ => false);
                });
            });
        }
    }

    /// <summary>
    /// Orleans Grain 中 JsonNode 序列化的集成测试。
    /// 这些测试验证在通过 Grain 方法调用传递 JsonNode 类型时，GitHub issue #9568 的修复能否正确工作，
    /// 尤其是向 JsonNode 参数传递 JsonValue 的情况。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    [Trait("Category", "BVT")]
    public class JsonNodeGrainTests : IClassFixture<JsonNodeGrainTestFixture>
    {
        private readonly JsonNodeGrainTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public JsonNodeGrainTests(JsonNodeGrainTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// 复现 GitHub issue #9568：测试具有 JsonNode 参数的 Grain 方法能否接收 JsonValue。
        /// 此测试验证核心修复：将 JsonValue（派生类型）传递给需要 JsonNode（基类型）的方法。
        /// 此前这会在反序列化期间抛出 InvalidCastException。
        /// </summary>
        [Fact]
        public async Task GrainCanProcessJsonValue_AsJsonNode()
        {
            // 当 Grain 方法具有 JsonNode 参数但收到 JsonValue 时
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(1);

            // 创建 JsonValue（它是 JsonNode 的子类型）
            JsonNode jsonNode = JsonValue.Create(1);

            // 这不应抛出 InvalidCastException
            var result = await grain.ProcessJsonNode(jsonNode);

            // 验证结果
            Assert.NotNull(result);
            Assert.Equal(1, result.GetValue<int>());
        }

        /// <summary>
        /// 测试 Grain 能否处理所有 JsonNode 子类型（JsonValue、JsonArray、JsonObject）。
        /// 验证每种类型经过 Grain 方法调用后均能保留其结构和值。
        /// </summary>
        [Fact]
        public async Task GrainCanProcessVariousJsonNodeTypes()
        {
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(2);

            // 测试包含 int 的 JsonValue
            JsonNode intValue = JsonValue.Create(42);
            var intResult = (await grain.ProcessJsonNode(intValue))!;
            Assert.Equal(42, intResult.GetValue<int>());

            // 测试包含 string 的 JsonValue
            JsonNode stringValue = JsonValue.Create("hello");
            var stringResult = (await grain.ProcessJsonNode(stringValue))!;
            Assert.Equal("hello", stringResult.GetValue<string>());

            // 测试包含 bool 的 JsonValue
            JsonNode boolValue = JsonValue.Create(true);
            var boolResult = (await grain.ProcessJsonNode(boolValue))!;
            Assert.True(boolResult.GetValue<bool>());

            // 测试 JsonArray
            JsonNode arrayValue = new JsonArray { 1, 2, 3 };
            var arrayResult = await grain.ProcessJsonNode(arrayValue);
            Assert.IsType<JsonArray>(arrayResult);
            Assert.Equal(3, ((JsonArray)arrayResult).Count);

            // 测试 JsonObject
            JsonNode objectValue = new JsonObject { ["key"] = "value", ["number"] = 123 };
            var objectResult = await grain.ProcessJsonNode(objectValue);
            Assert.IsType<JsonObject>(objectResult);
            Assert.Equal("value", objectResult["key"]!.GetValue<string>());
            Assert.Equal(123, objectResult["number"]!.GetValue<int>());
        }

        /// <summary>
        /// 测试 Grain 对复杂嵌套 JSON 结构的处理。
        /// 验证包含混合 JsonNode 类型的深层嵌套对象在 Grain 调用中能否正确
        /// 序列化及反序列化。
        /// </summary>
        [Fact]
        public async Task GrainCanProcessComplexJsonStructure()
        {
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(3);

            // 创建复杂的嵌套结构
            var complexNode = new JsonObject
            {
                ["simpleValue"] = JsonValue.Create(123),
                ["arrayWithValues"] = new JsonArray
                {
                    JsonValue.Create("first"),
                    JsonValue.Create(456),
                    JsonValue.Create(false)
                },
                ["nestedObject"] = new JsonObject
                {
                    ["deepValue"] = JsonValue.Create(3.14159),
                    ["deepArray"] = new JsonArray { JsonValue.Create(true), null }
                }
            };

            // 通过 Grain 进行处理
            var result = await grain.ProcessJsonNode(complexNode);

            // 验证结构得到保留
            Assert.NotNull(result);
            Assert.Equal(123, result["simpleValue"]!.GetValue<int>());

            var array = result["arrayWithValues"] as JsonArray;
            Assert.NotNull(array);
            Assert.Equal("first", array![0]!.GetValue<string>());
            Assert.Equal(456, array[1]!.GetValue<int>());
            Assert.False(array[2]!.GetValue<bool>());

            var nested = result["nestedObject"] as JsonObject;
            Assert.NotNull(nested);
            Assert.Equal(3.14159, nested!["deepValue"]!.GetValue<double>(), 5);
        }

        /// <summary>
        /// 测试是否可以在 Grain 方法内将 JsonNode 实例转换为字符串。
        /// 验证 Grain 收到的是可进行操作的有效 JsonNode 实例。
        /// </summary>
        [Fact]
        public async Task GrainCanConvertJsonNodeToString()
        {
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(4);

            // 使用 JsonValue 进行测试
            JsonNode valueNode = JsonValue.Create(42);
            var valueString = await grain.GetJsonString(valueNode);
            Assert.Equal("42", valueString);

            // 使用 JsonObject 进行测试
            JsonNode objectNode = new JsonObject { ["test"] = "value" };
            var objectString = await grain.GetJsonString(objectNode);
            Assert.Contains("\"test\":\"value\"", objectString);

            // 使用 JsonArray 进行测试
            JsonNode arrayNode = new JsonArray { 1, 2, 3 };
            var arrayString = await grain.GetJsonString(arrayNode);
            Assert.Equal("[1,2,3]", arrayString);
        }

        /// <summary>
        /// 测试 Grain 方法能否正确处理值为 null 的 JsonNode 参数。
        /// 确保 null 值能够正确序列化且不会导致异常。
        /// </summary>
        [Fact]
        public async Task GrainHandlesNullJsonNode()
        {
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(5);

            JsonNode? nullNode = null;
            var result = await grain.ProcessJsonNode(nullNode);
            Assert.Null(result);

            var stringResult = await grain.GetJsonString(nullNode);
            Assert.Equal("null", stringResult);
        }

        /// <summary>
        /// 测试使用 JsonNode 参数并发调用 Grain 时的线程安全性及正确性。
        /// 验证使用不同 JsonNode 值同时发起多次 Grain 调用不会相互干扰。
        /// </summary>
        [Fact]
        public async Task MultipleConcurrentGrainCallsWithJsonNodes()
        {
            // 测试并发调用，以确保线程安全
            var tasks = new Task[10];

            for (int i = 0; i < 10; i++)
            {
                var grainId = i + 100;
                var value = i * 10;
                tasks[i] = Task.Run(async () =>
                {
                    var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(grainId);
                    JsonNode node = JsonValue.Create(value);
                    var result = (await grain.ProcessJsonNode(node))!;
                    Assert.Equal(value, result.GetValue<int>());
                }, TestContext.Current.CancellationToken);
            }

            await Task.WhenAll(tasks);
        }
    }
}
