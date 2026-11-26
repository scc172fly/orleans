
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Orleans.Providers
{
    /// <summary>
    /// 实施该接口负责对 MemoryMessageBody 对象进行序列排序
    /// </summary>
    public interface IMemoryMessageBodySerializer
    {
        /// <summary>
        /// 将 <see cref="MemoryMessageBody"/> 序列化为字节的数组段。
        /// </summary>
        /// <param name="body">尸体 尸体 尸体 尸体</param>
        /// <returns>序列数据</returns>
        ArraySegment<byte> Serialize(MemoryMessageBody body);

        /// <summary>
        /// 将数组段降序成 <see cref="MemoryMessageBody"/>
        /// </summary>
        /// <param name="bodyBytes">体字节。</param>
        /// <returns>消散的通讯机体</returns>
        MemoryMessageBody Deserialize(ArraySegment<byte> bodyBytes);
    }

    /// <summary>
    /// 默认 <see cref="IMemoryMessageBodySerializer"/> 执行 。
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
        /// <param name="serializer">序列器。</param>
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
            // 序列只接受非中值 MemoryMessageBody。
            return serializer.Deserialize(bodyBytes)!;
        }

        /// <inheritdoc />
        void IOnDeserialized.OnDeserialized(DeserializationContext context)
        {
            this.serializer = context.ServiceProvider.GetRequiredService<Serializer<MemoryMessageBody>>();
        }
    }

    /// <summary>
    /// 用于内线流提供者的邮件正文 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class MemoryMessageBody
    {
        /// <summary>
        /// 初始化 <see cref="MemoryMessageBody"/> 类的新实例。
        /// </summary>
        /// 属于此信件内容的事件<param name="events">。</param>
        /// <param name="requestContext">发送此信息的背景。</param>        
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
        /// 获取信息请求的背景。
        /// </summary>
        [Id(1)]
        public Dictionary<string, object>? RequestContext { get; }
    }
}
