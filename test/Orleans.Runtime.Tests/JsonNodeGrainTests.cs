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
    /// Grain 测试界面 JsonNode 通过 Orleans grain 电话
    /// 此接口从 GitHub 质号#9568 复制假设情景, 其中的 JsonNode 参数
    /// 用于 grain 方法,可在运行时接收 JsonValue 实例。
    /// </summary>
    public interface IJsonNodeTestGrain : IGrainWithIntegerKey
    {
        Task<JsonNode?> ProcessJsonNode(JsonNode? node);
        Task<string> GetJsonString(JsonNode? node);
    }

    /// <summary>
    /// 执行 JsonNode 测试 grain。
    /// 提供测试 JsonNode 实例序列化和脱序的方法
    /// 通过 grain 方法调用,确保问题#9568的固定在分布背景下进行。
    /// </summary>
    public class JsonNodeTestGrain : Grain, IJsonNodeTestGrain
    {
        public Task<JsonNode?> ProcessJsonNode(JsonNode? node)
        {
            // 简单地返回节点 - 序列化应该处理
            return Task.FromResult(node);
        }

        public Task<string> GetJsonString(JsonNode? node)
        {
            // 转换为字符串以验证我们收到正确数据
            return Task.FromResult(node?.ToJsonString() ?? "null");
        }
    }

    /// <summary>
    /// 用于配置内处理 Orleans 集的测试固定装置,并配有 JSON 序列化支持。
    /// 两者兼设 silo 客户端使用 JsonCodec 用于 System.Text.Json 类型。
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
    /// 综合测试 JsonNode 中 Orleans grains.
    /// 这些测试核实 GitHub 问题#9568的确定值在 JsonNode 类型下正确运行
    /// 是通过 grain 方法调用,特别是当 JsonValue 被传送到 JsonNode 参数时。
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
        /// 生产 GitHub #9568:测试 grain 方法 JsonNode 能够接收参数 JsonValue.
        /// 本测试核查核心固定点, 即 JsonValue (衍生类型) 被传递到预期的方法 JsonNode (基准类型)。
        /// 在此之前,这将在消除航空化期间投掷 InvalidCastException 。
        /// </summary>
        [Fact]
        public async Task GrainCanProcessJsonValue_AsJsonNode()
        {
            // 当 grain 方法有一个 JsonNode 参数但收到一个 JsonValue时
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(1);

            // 创建 JsonValue (这是 JsonNode的子类型)
            JsonNode jsonNode = JsonValue.Create(1);

            // 这不应该扔出 InvalidCastException
            var result = await grain.ProcessJsonNode(jsonNode);

            // 核查结果
            Assert.NotNull(result);
            Assert.Equal(1, result.GetValue<int>());
        }

        /// <summary>
        /// 测试 grains 能够处理所有 JsonNode 子类型(JsonValue、 JsonArray、 JsonObject)。
        /// 证明每种类型通过 grain 方法调用保持其结构和数值。
        /// </summary>
        [Fact]
        public async Task GrainCanProcessVariousJsonNodeTypes()
        {
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(2);

            // 带有内注的测试 JsonValue
            JsonNode intValue = JsonValue.Create(42);
            var intResult = (await grain.ProcessJsonNode(intValue))!;
            Assert.Equal(42, intResult.GetValue<int>());

            // 用字符串测试 JsonValue
            JsonNode stringValue = JsonValue.Create("hello");
            var stringResult = (await grain.ProcessJsonNode(stringValue))!;
            Assert.Equal("hello", stringResult.GetValue<string>());

            // 用布薄测试 JsonValue
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
        /// 测试 grain 复杂嵌套处理 JSON 结构。
        /// 证明含有混合型号 JsonNode 的深嵌套物体正确无误
        /// 通过 grain 电话进行序列和消毒。
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

            // 通过 grain进程
            var result = await grain.ProcessJsonNode(complexNode);

            // 保存了校验结构
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
        /// 测试 JsonNode 实例可在 grain 方法内转换成字符串。
        /// 证明 grain 收到可被操纵的有效 JsonNode 实例。
        /// </summary>
        [Fact]
        public async Task GrainCanConvertJsonNodeToString()
        {
            var grain = _fixture.GrainFactory.GetGrain<IJsonNodeTestGrain>(4);

            // 与 JsonValue测试
            JsonNode valueNode = JsonValue.Create(42);
            var valueString = await grain.GetJsonString(valueNode);
            Assert.Equal("42", valueString);

            // 与 JsonObject测试
            JsonNode objectNode = new JsonObject { ["test"] = "value" };
            var objectString = await grain.GetJsonString(objectNode);
            Assert.Contains("\"test\":\"value\"", objectString);

            // 与 JsonArray测试
            JsonNode arrayNode = new JsonArray { 1, 2, 3 };
            var arrayString = await grain.GetJsonString(arrayNode);
            Assert.Equal("[1,2,3]", arrayString);
        }

        /// <summary>
        /// 测试 grain 方法正确处理 null JsonNode 参数。
        /// 确保无效价值适当序列,不造成例外。
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
        /// 试验线安全和正确性同时进行 grain 电话用 JsonNode 参数。
        /// 证明多个同时 grain 调用不同值的 JsonNode 并不相互干扰。
        /// </summary>
        [Fact]
        public async Task MultipleConcurrentGrainCallsWithJsonNodes()
        {
            // 测试同时调用以确保线串安全
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
