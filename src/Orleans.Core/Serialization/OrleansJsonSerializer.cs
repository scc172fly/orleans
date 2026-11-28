using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans.GrainReferences;

namespace Orleans.Serialization
{

    /// <summary>
    /// 使用为 Orleans 类型配置的 Newtonsoft.Json 设置来序列化和反序列化值。
    /// </summary>
    public class OrleansJsonSerializer
    {
        /// <summary>
        /// 用于选择完整程序集限定类型名称的配置属性名称。
        /// </summary>
        public const string UseFullAssemblyNamesProperty = "UseFullAssemblyNames";

        /// <summary>
        /// 用于选择缩进式 JSON 格式的配置属性名称。
        /// </summary>
        public const string IndentJsonProperty = "IndentJSON";

        /// <summary>
        /// 用于选择类型名称如何包含在 JSON 中的配置属性名称。
        /// </summary>
        public const string TypeNameHandlingProperty = "TypeNameHandling";
        private readonly JsonSerializerSettings settings;

        /// <summary>
        /// 初始化 <see cref="OrleansJsonSerializer"/> 类的新实例。
        /// </summary>
        /// <param name="options">已配置的 JSON 序列化器选项。</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
        public OrleansJsonSerializer(IOptions<OrleansJsonSerializerOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            this.settings = options.Value.JsonSerializerSettings;
        }

        /// <summary>
        /// 从提供的输入反序列化指定预期类型的对象。
        /// </summary>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="input">输入。</param>
        /// <returns>反序列化后的对象。</returns>
        public object? Deserialize(Type expectedType, string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            return JsonConvert.DeserializeObject(input, expectedType, this.settings);
        }

        /// <summary>
        /// 从提供的流反序列化指定预期类型的对象。
        /// </summary>
        /// <param name="expectedType">预期类型。</param>
        /// <param name="input">输入流。</param>
        /// <returns>反序列化后的对象。</returns>
        public object? Deserialize(Type expectedType, Stream input)
        {
            using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            using var jsonReader = new JsonTextReader(reader);
            var serializer = JsonSerializer.Create(this.settings);
            return serializer.Deserialize(jsonReader, expectedType);
        }

        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="item">要序列化的对象。</param>
        /// <param name="expectedType">反序列化器应预期的类型。</param>
        /// <returns><paramref name="item"/> 的 JSON 表示形式。</returns>
        public string Serialize(object? item, Type expectedType) => JsonConvert.SerializeObject(item, expectedType, this.settings);

        /// <summary>
        /// 将对象序列化到流中。
        /// </summary>
        /// <param name="item">要序列化的对象。</param>
        /// <param name="expectedType">反序列化器应预期的类型。</param>
        /// <param name="destination">目标流。</param>
        public void Serialize(object? item, Type expectedType, Stream destination)
        {
            using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(writer);
            var serializer = JsonSerializer.Create(this.settings);
            serializer.Serialize(jsonWriter, item, expectedType);
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="IPAddress"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class IPAddressConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IPAddress));
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            IPAddress ip = (IPAddress)value;
            writer.WriteValue(ip.ToString());
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            return IPAddress.Parse(token.Value<string>()!);
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="GrainId"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class GrainIdConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType) => objectType == typeof(GrainId);

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            GrainId id = (GrainId)value;
            writer.WriteStartObject();
            writer.WritePropertyName("Type");
            writer.WriteValue(id.Type.ToString());
            writer.WritePropertyName("Key");
            writer.WriteValue(id.Key.ToString());
            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            GrainId grainId = GrainId.Create(jo["Type"]!.ToObject<string>()!, jo["Key"]!.ToObject<string>()!);
            return grainId;
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="ActivationId"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class ActivationIdConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType) => objectType == typeof(ActivationId);

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            ActivationId id = (ActivationId)value;
            writer.WriteValue(id.ToParsableString());
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return reader.Value switch
            {
                string { Length: > 0 } str => ActivationId.FromParsableString(str),
                _ => default
            };
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="SiloAddress"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class SiloAddressJsonConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(SiloAddress));
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            SiloAddress addr = (SiloAddress)value;
            writer.WriteValue(addr.ToParsableString());
        }

        /// <inheritdoc/>
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(reader);
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    var jo = JObject.Load(reader);
                    return SiloAddress.FromParsableString(jo["SiloAddress"]!.ToObject<string>()!);
                case JsonToken.String:
                    return SiloAddress.FromParsableString((reader.Value as string)!);
            }

            return null;
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="MembershipVersion"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class MembershipVersionJsonConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType) => objectType == typeof(MembershipVersion);

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            MembershipVersion typedValue = (MembershipVersion)value;
            writer.WriteValue(typedValue.Value);
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return reader.Value switch
            {
                long l => new MembershipVersion(l),
                _ => default
            };
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="UniqueKey"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class UniqueKeyConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(UniqueKey));
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            UniqueKey key = (UniqueKey)value;
            writer.WriteStartObject();
            writer.WritePropertyName("UniqueKey");
            writer.WriteValue(key.ToHexString());
            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            UniqueKey addr = UniqueKey.Parse(jo["UniqueKey"]!.ToObject<string>()!.AsSpan());
            return addr;
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="IPEndPoint"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class IPEndPointConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IPEndPoint));
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(serializer);
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            IPEndPoint ep = (IPEndPoint)value;
            writer.WriteStartObject();
            writer.WritePropertyName("Address");
            serializer.Serialize(writer, ep.Address);
            writer.WritePropertyName("Port");
            writer.WriteValue(ep.Port);
            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            IPAddress address = jo["Address"]!.ToObject<IPAddress>(serializer)!;
            int port = jo["Port"]!.Value<int>();
            return new IPEndPoint(address, port);
        }
    }

    /// <summary>
    /// <see cref="Newtonsoft.Json.JsonConverter" /> 的实现，用于 <see cref="GrainReference"/>。
    /// </summary>
    /// <seealso cref="Newtonsoft.Json.JsonConverter" />
    public class GrainReferenceJsonConverter : JsonConverter
    {
        private static readonly Type AddressableType = typeof(IAddressable);
        private readonly GrainReferenceActivator referenceActivator;

        /// <summary>
        /// 初始化 <see cref="GrainReferenceJsonConverter"/> 类的新实例。
        /// </summary>
        /// <param name="referenceActivator">Grain 引用激活器。</param>
        public GrainReferenceJsonConverter(GrainReferenceActivator referenceActivator)
        {
            this.referenceActivator = referenceActivator;
        }

        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return AddressableType.IsAssignableFrom(objectType);
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            var val = ((IAddressable)value!).AsReference();
            writer.WriteStartObject();
            writer.WritePropertyName("Id");
            writer.WriteStartObject();
            writer.WritePropertyName("Type");
            writer.WriteValue(val.GrainId.Type.ToString());
            writer.WritePropertyName("Key");
            writer.WriteValue(val.GrainId.Key.ToString());
            writer.WriteEndObject();
            writer.WritePropertyName("Interface");
            writer.WriteValue(val.InterfaceType.ToString());
            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            var id = jo["Id"];
            GrainId grainId = GrainId.Create(id!["Type"]!.ToObject<string>()!, id["Key"]!.ToObject<string>()!);
            var encodedInterface = jo["Interface"]!.ToString();
            var iface = string.IsNullOrWhiteSpace(encodedInterface) ? default : GrainInterfaceType.Create(encodedInterface);
            return this.referenceActivator.CreateReference(grainId, iface);
        }
    }
}
