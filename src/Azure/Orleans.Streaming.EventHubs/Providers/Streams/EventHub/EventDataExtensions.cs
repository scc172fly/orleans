using System;
using System.Collections.Generic;
using Azure.Messaging.EventHubs;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 延伸 EventData 以支持流流
    /// </summary>
    public static class EventDataExtensions
    {
        private const string EventDataPropertyStreamNamespaceKey = "StreamNamespace";

        /// <summary>
        /// 将流行命名空间添加到 EventData
        /// </summary>
        /// <param name="eventData">需要更新的事件数据。</param>
        /// <param name="streamNamespace">要存储的流命名空间值。</param>
        public static void SetStreamNamespaceProperty(this EventData eventData, string? streamNamespace)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            eventData.Properties[EventDataPropertyStreamNamespaceKey] = streamNamespace;
        }

        /// <summary>
        /// 从 EventData获取流行命名空间
        /// </summary>
        /// <param name="eventData">事件数据读取。</param>
        /// <returns>存储流命名空间,如果没有,则 <see langword="null"/> 。</returns>
        public static string? GetStreamNamespaceProperty(this EventData eventData)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            object? namespaceObj;
            if (eventData.Properties.TryGetValue(EventDataPropertyStreamNamespaceKey, out namespaceObj))
            {
                return (string)namespaceObj;
            }
            return null;
        }

        /// <summary>
        /// 序列化事件数据属性
        /// </summary>
        /// <param name="eventData">包含序列化属性的事件数据。</param>
        /// <param name="serializer">用于对应用程序属性序列排序的序列器。</param>
        public static byte[] SerializeProperties(this EventData eventData, Serialization.Serializer serializer)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            ArgumentNullException.ThrowIfNull(serializer);
            if (eventData.Properties.Count == 0
                || (eventData.Properties.Count == 1 && eventData.Properties.ContainsKey(EventDataPropertyStreamNamespaceKey)))
            {
                return [];
            }

            var properties = new List<KeyValuePair<string, object>>(eventData.Properties.Count);
            foreach (var property in eventData.Properties)
            {
                if (!string.Equals(property.Key, EventDataPropertyStreamNamespaceKey, StringComparison.Ordinal))
                {
                    properties.Add(property);
                }
            }

            return serializer.SerializeToArray(properties);
        }

        /// <summary>
        /// 降低事件数据属性的序列
        /// </summary>
        /// <param name="bytes">序列属性。</param>
        /// <param name="serializer">用于解除应用程序属性的序列器。</param>
        public static IDictionary<string, object> DeserializeProperties(this ArraySegment<byte> bytes, Serialization.Serializer serializer)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            if (bytes.Count == 0)
            {
                return new Dictionary<string, object>();
            }

            var properties = serializer.Deserialize<List<KeyValuePair<string, object>>>(bytes.AsSpan())!;
            var result = new Dictionary<string, object>(properties.Count);
            foreach (var property in properties)
            {
                result.Add(property.Key, property.Value);
            }

            return result;
        }
    }
}
