using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TestKit;
using Xunit;

// TestKit 的旧版 TestValues 契约无法表达刻意设置为可空的测试值。
namespace Orleans.Serialization.UnitTests
{
    /// <summary>
    /// Orleans 对 Newtonsoft.Json 序列化的支持测试。
    /// 
    /// Orleans 集成 Newtonsoft.Json（Json.NET），以支持以下场景：
    /// - 需要兼容现有 Newtonsoft.Json 配置
    /// - 需要 System.Text.Json 尚不具备的高级 JSON 功能
    /// - 需要与已使用 Newtonsoft.Json 的系统集成
    /// 
    /// Orleans 中的 Newtonsoft.Json 编解码器：
    /// - 支持全部 Json.NET 功能，包括自定义转换器
    /// - 可使用类型谓词有选择地应用
    /// - 同时提供序列化和深度复制功能
    /// - 处理 JObject、JArray 等 Json.NET 专用类型
    /// 
    /// 对于已经具有复杂 Newtonsoft.Json 配置的现有系统，
    /// 在迁移到 Orleans 时，此集成尤其有用。
    /// </summary>
    [Trait("Category", "BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Serialization")]
    public class NewtonsoftJsonCodecTests : FieldCodecTester<MyNewtonsoftJsonClass, IFieldCodec<MyNewtonsoftJsonClass>>, IClassFixture<SerializationTesterFixture>
    {
        public NewtonsoftJsonCodecTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
        {
        }

        protected override void Configure(ISerializerBuilder builder)
        {
            builder.AddNewtonsoftJsonSerializer(isSupported: type => type.GetCustomAttribute<MyNewtonsoftJsonSerializableAttribute>(inherit: false) is not null);
        }

        protected override MyNewtonsoftJsonClass CreateValue() => new MyNewtonsoftJsonClass { IntProperty = 30, SubTypeProperty = "hello" };

        protected override int[] MaxSegmentSizes => new[] { 840 };

        protected override MyNewtonsoftJsonClass[] TestValues => new MyNewtonsoftJsonClass[]
        {
            null!,
            new MyNewtonsoftJsonClass(),
            new MyNewtonsoftJsonClass() { IntProperty = 150, SubTypeProperty = new string('c', 20) },
            new MyNewtonsoftJsonClass() { IntProperty = -150_000, SubTypeProperty = new string('c', 4097) },
        };

        [Fact]
        public void NewtonsoftJsonDeepCopyTyped()
        {
            var original = new MyNewtonsoftJsonClass { IntProperty = 30, SubTypeProperty = "hi" };
            var copier = ServiceProvider.GetRequiredService<DeepCopier<MyNewtonsoftJsonClass>>();
            var result = copier.Copy(original)!;

            Assert.Equal(original.IntProperty, result.IntProperty);
            Assert.Equal(original.SubTypeProperty, result.SubTypeProperty);
        }

        [Fact]
        public void NewtonsoftJsonDeepCopyUntyped()
        {
            var original = new MyNewtonsoftJsonClass { IntProperty = 30, SubTypeProperty = "hi" };
            var copier = ServiceProvider.GetRequiredService<DeepCopier>();
            var result = (MyNewtonsoftJsonClass)copier.Copy((object)original)!;

            Assert.Equal(original.IntProperty, result.IntProperty);
            Assert.Equal(original.SubTypeProperty, result.SubTypeProperty);
        }

        [Fact]
        public void NewtonsoftJsonRoundTripThroughCodec()
        {
            var original = new MyNewtonsoftJsonClass { IntProperty = 30, SubTypeProperty = "hi" };
            var result = RoundTripThroughCodec(original)!;

            Assert.Equal(original.IntProperty, result.IntProperty);
            Assert.Equal(original.SubTypeProperty, result.SubTypeProperty);
        }

        [Fact]
        public void NewtonsoftJsonRoundTripThroughUntypedSerializer()
        {
            var original = new MyNewtonsoftJsonClass { IntProperty = 30, SubTypeProperty = "hi" };
            var untypedResult = RoundTripThroughUntypedSerializer(original, out _);

            var result = Assert.IsType<MyNewtonsoftJsonClass>(untypedResult);
            Assert.Equal(original.IntProperty, result.IntProperty);
            Assert.Equal(original.SubTypeProperty, result.SubTypeProperty);
        }

        [Fact]
        public void CanSerializeNativeJsonTypes()
        {
            var jsonArray = Newtonsoft.Json.JsonConvert.DeserializeObject<JArray>("[1, true, \"three\", {\"foo\": \"bar\"}]");
            var jsonObject = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>("{\"foo\": \"bar\"}");
            var copier = ServiceProvider.GetRequiredService<DeepCopier>();

            var deserializedArray = RoundTripThroughUntypedSerializer(jsonArray, out _);
            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(jsonArray), Newtonsoft.Json.JsonConvert.SerializeObject(deserializedArray));

            var deserializedObject = RoundTripThroughUntypedSerializer(jsonObject, out _);
            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(jsonObject), Newtonsoft.Json.JsonConvert.SerializeObject(deserializedObject));
        }

        [Fact]
        public void CanSerializeNativeJsonBaseTypes()
        {
            JToken original = JToken.Parse("{\"foo\":\"bar\",\"items\":[1,true,\"three\"]}");
            var result = RoundTripThroughCodec(original);

            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(original), Newtonsoft.Json.JsonConvert.SerializeObject(result));
        }
    }
    [Trait("Category", "BVT")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Serialization")]
    public class NewtonsoftJsonCodecCopierTests : CopierTester<MyNewtonsoftJsonClass, IDeepCopier<MyNewtonsoftJsonClass>>, IClassFixture<SerializationTesterFixture>
    {
        public NewtonsoftJsonCodecCopierTests(ITestOutputHelper output, SerializationTesterFixture fixture) : base(output, fixture)
        {
        }

        protected override void Configure(ISerializerBuilder builder)
        {
            builder.AddNewtonsoftJsonSerializer(isSupported: type => type.GetCustomAttribute<MyNewtonsoftJsonSerializableAttribute>(inherit: false) is not null);
        }

        protected override IDeepCopier<MyNewtonsoftJsonClass> CreateCopier() => ServiceProvider.GetRequiredService<ICodecProvider>().GetDeepCopier<MyNewtonsoftJsonClass>();

        protected override MyNewtonsoftJsonClass CreateValue() => new MyNewtonsoftJsonClass { IntProperty = 30, SubTypeProperty = "hello" };

        protected override MyNewtonsoftJsonClass[] TestValues => new MyNewtonsoftJsonClass[]
        {
            null!,
            new MyNewtonsoftJsonClass(),
            new MyNewtonsoftJsonClass() { IntProperty = 150, SubTypeProperty = new string('c', 20) },
            new MyNewtonsoftJsonClass() { IntProperty = -150_000, SubTypeProperty = new string('c', 4097) },
        };

        [Fact]
        public void CanCopyNativeJsonTypes()
        {
            var jsonArray = Newtonsoft.Json.JsonConvert.DeserializeObject<JArray>("[1, true, \"three\", {\"foo\": \"bar\"}]");
            var jsonObject = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>("{\"foo\": \"bar\"}");
            var copier = ServiceProvider.GetRequiredService<DeepCopier>();

            var deserializedArray = copier.Copy(jsonArray);
            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(jsonArray), Newtonsoft.Json.JsonConvert.SerializeObject(deserializedArray));

            var deserializedObject = copier.Copy(jsonObject);
            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(jsonObject), Newtonsoft.Json.JsonConvert.SerializeObject(deserializedObject));
        }

        [Fact]
        public void CanCopyNativeJsonBaseTypes()
        {
            JToken original = JToken.Parse("{\"foo\":\"bar\",\"items\":[1,true,\"three\"]}");
            var copier = ServiceProvider.GetRequiredService<DeepCopier<JToken>>();
            var result = copier.Copy(original);

            Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(original), Newtonsoft.Json.JsonConvert.SerializeObject(result));
        }
    }
}
