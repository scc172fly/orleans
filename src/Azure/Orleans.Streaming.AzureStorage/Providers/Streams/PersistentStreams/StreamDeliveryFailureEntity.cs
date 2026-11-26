using System;
using Azure;
using Azure.Data.Tables;
using Orleans.Serialization;
using Orleans.Streams;

namespace Orleans.Providers.Streams.PersistentStreams
{
    /// <summary>
    /// 交付表存储实体。
    /// </summary>
    public class StreamDeliveryFailureEntity : ITableEntity
    {
        /// <summary>
        /// 获取或设置表格分区密钥, 以识别流提供方和部署 。
        /// </summary>
        public string PartitionKey { get; set; } = null!;

        /// <summary>
        /// 获取或设置表格行键,确定并订购交付失败。
        /// </summary>
        public string RowKey { get; set; } = null!;

        /// <summary>
        /// 获取或设置 Azure 表格服务所维护的时间戳。
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// 获取或设置用于乐观货币的实体标签。
        /// </summary>
        public ETag ETag { get; set; }

        /// <summary>
        /// 发生交付失败的订阅日期的ID。
        /// </summary>
        public Guid SubscriptionId { get; set; }

        /// <summary>
        /// 生成此失败的流源提供者的名称 。
        /// </summary>
        public string StreamProviderName { get; set; } = null!;

        /// <summary>
        /// 引导失败的河流
        /// </summary>
        public string StreamGuid { get; set; } = null!;

        /// <summary>
        /// 失败发生时所在的流的命名空间 。
        /// </summary>
        public string? StreamNamespace { get; set; }

        /// <summary>
        /// 无法交付的事件的序列符号 。
        /// </summary>
        public byte[]? SequenceToken { get; set; }

        /// <summary>
        /// 在持续调用前设定分区密钥 。
        /// </summary>
        public virtual void SetPartitionKey(string deploymentId)
        {
            PartitionKey = MakeDefaultPartitionKey(StreamProviderName, deploymentId);
        }

        /// <summary>
        /// 默认分区密钥
        /// </summary>
        public static string MakeDefaultPartitionKey(string streamProviderName, string deploymentId)
        {
            return $"DeliveryFailure_{streamProviderName}_{deploymentId}";
        }

        /// <summary>
        /// 在持续调用前设定行键
        /// </summary>
        public virtual void SetRowkey()
        {
            RowKey = $"{ReverseOrderTimestampTicks():x16}_{Guid.NewGuid()}";
        }

        /// <summary>
        /// 设置序列符号, 将其序列序列编号为属性 。
        /// </summary>
        /// <param name="serializer"></param>
        /// <param name="token"></param>
        public virtual void SetSequenceToken(Serializer<StreamSequenceToken> serializer, StreamSequenceToken? token)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            SequenceToken = token != null ? serializer.SerializeToArray(token) : null;
        }

        /// <summary>
        /// 将序列符号从属性中解密, 以获取顺序符。
        /// </summary>
        /// <returns></returns>
        public virtual StreamSequenceToken? GetSequenceToken(Serializer<StreamSequenceToken> serializer)
        {
            ArgumentNullException.ThrowIfNull(serializer);
            return SequenceToken != null ? serializer.Deserialize(SequenceToken) : null;
        }

        /// <summary>
        /// 返回从现在( UTC) 到9683年的秒数。
        /// </summary>
        /// <remarks>
        /// 用于在分区起始时排序最近的失败。 虽然有用, 但可以
        ///  高故障负荷下,在重负载下,这可能导致
        ///  表格中。这不是预期发生的情况,但如果发生了,我们建议对您的行进行分解
        ///  键,使用其他字段(流命名空间?) 。
        /// </remarks>
        /// <returns></returns>
        protected static long ReverseOrderTimestampTicks()
        {
            var now = DateTime.UtcNow;
            return DateTime.MaxValue.Ticks - now.Ticks;
        }
    }
}
