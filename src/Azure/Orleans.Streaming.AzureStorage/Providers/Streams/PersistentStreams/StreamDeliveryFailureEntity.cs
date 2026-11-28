using System;
using Azure;
using Azure.Data.Tables;
using Orleans.Serialization;
using Orleans.Streams;

namespace Orleans.Providers.Streams.PersistentStreams
{
    /// <summary>
    /// 投递失败的表存储实体。
    /// </summary>
    public class StreamDeliveryFailureEntity : ITableEntity
    {
        /// <summary>
        /// 获取或设置用于标识流提供程序和部署的表分区键。
        /// </summary>
        public string PartitionKey { get; set; } = null!;

        /// <summary>
        /// 获取或设置用于标识投递失败记录并确定其顺序的表行键。
        /// </summary>
        public string RowKey { get; set; } = null!;

        /// <summary>
        /// 获取或设置由 Azure Table 服务维护的时间戳。
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// 获取或设置用于乐观并发控制的实体标记。
        /// </summary>
        public ETag ETag { get; set; }

        /// <summary>
        /// 发生此次投递失败的订阅 ID。
        /// </summary>
        public Guid SubscriptionId { get; set; }

        /// <summary>
        /// 产生此次失败的流提供程序名称。
        /// </summary>
        public string StreamProviderName { get; set; } = null!;

        /// <summary>
        /// 发生失败的流的 GUID 标识符。
        /// </summary>
        public string StreamGuid { get; set; } = null!;

        /// <summary>
        /// 发生失败的流的命名空间。
        /// </summary>
        public string? StreamNamespace { get; set; }

        /// <summary>
        /// 投递失败事件的序列化序列令牌。
        /// </summary>
        public byte[]? SequenceToken { get; set; }

        /// <summary>
        /// 在持久化调用前设置分区键。
        /// </summary>
        public virtual void SetPartitionKey(string deploymentId)
        {
            PartitionKey = MakeDefaultPartitionKey(StreamProviderName, deploymentId);
        }

        /// <summary>
        /// 默认分区键。
        /// </summary>
        public static string MakeDefaultPartitionKey(string streamProviderName, string deploymentId)
        {
            return $"DeliveryFailure_{streamProviderName}_{deploymentId}";
        }

        /// <summary>
        /// 在持久化调用前设置行键。
        /// </summary>
        public virtual void SetRowkey()
        {
            RowKey = $"{ReverseOrderTimestampTicks():x16}_{Guid.NewGuid()}";
        }

        /// <summary>
        /// 将序列令牌序列化到属性中以设置序列令牌。
        /// </summary>
        /// <param name="serializer"></param>
        /// <param name="token"></param>
        public virtual void SetSequenceToken(Serializer<StreamSequenceToken> serializer, StreamSequenceToken? token)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            SequenceToken = token != null ? serializer.SerializeToArray(token) : null;
        }

        /// <summary>
        /// 通过从属性反序列化获取序列令牌。
        /// </summary>
        /// <returns></returns>
        public virtual StreamSequenceToken? GetSequenceToken(Serializer<StreamSequenceToken> serializer)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            return SequenceToken != null ? serializer.Deserialize(SequenceToken) : null;
        }

        /// <summary>
        /// 返回从当前 UTC 时间到公元 9683 年之间的计时单位数（ticks）。
        /// </summary>
        /// <remarks>
        /// 这有助于将最近发生的失败排在分区开头。虽然它有利于高效的
        /// 表存储查询，但在大量失败的负载下，可能会导致表中出现
        /// 热点。这不是预期情况，但如果发生，建议使用其他字段进一步细分
        /// 行键（例如流命名空间）。
        /// </remarks>
        /// <returns></returns>
        protected static long ReverseOrderTimestampTicks()
        {
            var now = DateTime.UtcNow;
            return DateTime.MaxValue.Ticks - now.Ticks;
        }
    }
}
