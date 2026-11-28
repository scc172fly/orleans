
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Orleans.Providers
{
    /// <summary>
    /// 此接口的实现负责序列化 MemoryMessageBody 对象。
    /// </summary>
    public interface IMemoryMessageBodySerializer
    {
        /// <summary>
        /// 将 <see cref="MemoryMessageBody"/> 序列化为一个字节数组段。
        /// </summary>
        /// <param name="body">消息体。</param>
        /// <returns>序列化后的数据。</returns>
        ArraySegment<byte> Serialize(MemoryMessageBody body);

        /// <summary>
        /// 将数组段反序列化为 <see cref="MemoryMessageBody"/>。
        /// </summary>
        /// <param name="bodyBytes">消息体的字节数据。</param>
        /// <returns>反序列化后的消息体。</returns>
        MemoryMessageBody Deserialize(ArraySegment<byte> bodyBytes);
    }

    /// <summary>
    /// 默认 <see cref="IMemoryMessageBodySerializer"/> 实现。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    [SerializationCallbacks(typeof(Runtime.OnDeserializedCallbacks))]
    public sealed class DefaultMemoryMessageBodySerializer : IMemoryMessageBodySerializer, IOnDeserialized
    {
        [NonSerialized]
        private Serializer<MemoryMessageBody> serializer;

        /// <summary>
        /// 初始化 <see cref="DefaultMemoryMessageBodySerializer" /> 类的新实例。
        /// </summary>
        /// <param name="serializer">序列化器。</param>
        public DefaultMemoryMessageBodySerializer(Serializer<MemoryMessageBody> serializer)
        {
            this.serializer = serializer;
        }

        /// <inheritdoc />
        public ArraySegment<byte> Serialize(MemoryMessageBody body)
        {
            return new ArraySegment<byte>(serializer.SerializeToArray(body));
        }

        /// <inheritdoc />
        public MemoryMessageBody Deserialize(ArraySegment<byte> bodyBytes)
        {
            // Serialize 仅接受非 null 的 MemoryMessageBody。
            return serializer.Deserialize(bodyBytes)!;
        }

        /// <inheritdoc />
        void IOnDeserialized.OnDeserialized(DeserializationContext context)
        {
            this.serializer = context.ServiceProvider.GetRequiredService<Serializer<MemoryMessageBody>>();
        }
    }

    /// <summary>
    /// 内存流提供程序使用的消息体。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class MemoryMessageBody
    {
        /// <summary>
        /// 初始化 <see cref="MemoryMessageBody"/> 类的新实例。
        /// </summary>
        /// <param name="events">此消息包含的事件。</param>
        /// <param name="requestContext">发送此消息时的上下文。</param>
        public MemoryMessageBody(IEnumerable<object> events, Dictionary<string, object>? requestContext)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            Events = events.ToList();
            RequestContext = requestContext;
        }

        /// <summary>
        /// 获取消息中的事件。
        /// </summary>
        [Id(0)]
        public List<object> Events { get; }

        /// <summary>
        /// 获取消息的请求上下文。
        /// </summary>
        [Id(1)]
        public Dictionary<string, object>? RequestContext { get; }
    }
}
