using System;
using System.Collections.Generic;
using Azure.Messaging.EventHubs;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// 扩展 EventData 以支持流处理。
    /// </summary>
    public static class EventDataExtensions
    {
        private const string EventDataPropertyStreamNamespaceKey = "StreamNamespace";

        /// <summary>
        /// 将流命名空间添加到 EventData。
        /// </summary>
        /// <param name="eventData">要更新的事件数据。</param>
        /// <param name="streamNamespace">要存储的流命名空间值。</param>
        public static void SetStreamNamespaceProperty(this EventData eventData, string? streamNamespace)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            eventData.Properties[EventDataPropertyStreamNamespaceKey] = streamNamespace;
        }

        /// <summary>
        /// 从 EventData 获取流命名空间。
        /// </summary>
        /// <param name="eventData">要读取的事件数据。</param>
        /// <returns>已存储的流命名空间；不存在时返回 <see langword="null"/>。</returns>
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
        /// 序列化事件数据属性。
        /// </summary>
        /// <param name="eventData">包含要序列化属性的事件数据。</param>
        /// <param name="serializer">用于序列化应用程序属性的序列化器。</param>
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
        /// 反序列化事件数据属性。
        /// </summary>
        /// <param name="bytes">序列化后的属性。</param>
        /// <param name="serializer">用于反序列化应用程序属性的序列化器。</param>
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
