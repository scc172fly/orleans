using System;
using Newtonsoft.Json;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 表示流订阅的发布/订阅注册状态。
    /// </summary>
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    [GenerateSerializer]
    public sealed class PubSubSubscriptionState : IEquatable<PubSubSubscriptionState?>, System.Text.Json.Serialization.IJsonOnDeserialized
    {
        /// <summary>
        /// 标识流订阅的生命周期状态。
        /// </summary>
        public enum SubscriptionStates
        {
            /// <summary>
            /// 订阅处于活动状态。
            /// </summary>
            Active,

            /// <summary>
            /// 订阅处于故障状态。
            /// </summary>
            Faulted,
        }

        // 重要！！！！！
        // 为使 JSON 序列化正常工作，这些字段必须为 public 且不能为 readonly！
        // 如果将其中任何字段改为 readonly，则需实现 ISerializable。
        /// <summary>
        /// 订阅标识符。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("subscriptionId")]
        [Id(0)]
        public GuidId SubscriptionId;

        /// <summary>
        /// 已订阅流的限定标识符。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("stream")]
        [Id(1)]
        public QualifiedStreamId Stream;

        /// <summary>
        /// 消费该流的 Grain 的标识符。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("consumer")]
        [Id(2)]
        public GrainId Consumer; // 此字段的类型必须为公开类型，否则不会为该类生成 Orleans 序列化器。

        /// <summary>
        /// 与订阅关联的序列化筛选数据。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("filterData")]
        [Id(3)]
        public string? FilterData; // 已序列化的函数信息。

        /// <summary>
        /// 订阅的生命周期状态。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        [System.Text.Json.Serialization.JsonPropertyName("state")]
        [Id(4)]
        public SubscriptionStates state;

        /// <summary>
        /// 获取一个值，指示订阅是否处于故障状态。
        /// </summary>
        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFaulted { get { return state == SubscriptionStates.Faulted; } }

        // 为使 JSON 序列化正常工作，此构造函数必须为 public！
        // 如果将其改为非 public，则需实现 ISerializable。
        /// <summary>
        /// 初始化活动流订阅注册记录。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">限定流标识符。</param>
        /// <param name="streamConsumer">消费该流的 Grain 的标识符。</param>
        public PubSubSubscriptionState(
            GuidId subscriptionId,
            QualifiedStreamId streamId,
            GrainId streamConsumer)
        {
            SubscriptionId = subscriptionId;
            Stream = streamId;
            Consumer = streamConsumer;
            state = SubscriptionStates.Active;
        }

        /// <summary>
        /// 初始化流订阅注册记录。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="stream">限定流标识符。</param>
        /// <param name="consumer">消费该流的 Grain 的标识符。</param>
        /// <param name="filterData">与订阅关联的序列化筛选数据。</param>
        /// <param name="state">订阅的生命周期状态。</param>
        [JsonConstructor]
        [System.Text.Json.Serialization.JsonConstructor]
        public PubSubSubscriptionState(
            GuidId subscriptionId,
            QualifiedStreamId stream,
            GrainId consumer,
            string? filterData,
            SubscriptionStates state)
            : this(subscriptionId, stream, consumer)
        {
            FilterData = filterData;
            this.state = state;
        }

        /// <summary>
        /// 设置与订阅关联的序列化筛选数据。
        /// </summary>
        /// <param name="filterData">序列化筛选数据。</param>
        public void AddFilter(string? filterData)
        {
            this.FilterData = filterData;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            // 注意：不能对 PubSubSubscriptionState 使用 'as' 运算符，因为它是结构体。（译注：当前声明为 class，并非结构体。）
            return obj is PubSubSubscriptionState && Equals((PubSubSubscriptionState)obj);
        }

        /// <inheritdoc/>
        public bool Equals(PubSubSubscriptionState? other)
        {
            if ((object?)other == null)
                return false;
            // 注意：PubSubSubscriptionState 是结构体，因此 'other' 永远不会为 null。（译注：当前类型为 class，other 可以为 null；上方代码也明确进行了判空。）
            return Equals(other.SubscriptionId);
        }

        /// <summary>
        /// 判断此订阅是否具有指定标识符。
        /// </summary>
        /// <param name="subscriptionId">要比较的订阅标识符。</param>
        /// <returns>如果标识符相等，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public bool Equals(GuidId? subscriptionId)
        {
            if (ReferenceEquals(null, subscriptionId)) return false;
            return SubscriptionId.Equals(subscriptionId);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return SubscriptionId.GetHashCode();
        }

        /// <summary>
        /// 按订阅标识符比较两个订阅状态是否相等。
        /// </summary>
        /// <param name="left">第一个订阅状态。</param>
        /// <param name="right">第二个订阅状态。</param>
        /// <returns>
        /// 返回 <see langword="true"/> 表示两个值均为 <see langword="null"/>，或具有相同的订阅标识符；
        /// 否则返回 <see langword="false"/>。
        /// </returns>
        public static bool operator ==(PubSubSubscriptionState? left, PubSubSubscriptionState? right)
        {
            if ((object?)left == null && (object?)right == null)
                return true;
            if ((object?)left != null)
            {
                return left.Equals(right);
            }
            return false;
        }

        /// <summary>
        /// 按订阅标识符比较两个订阅状态是否不相等。
        /// </summary>
        /// <param name="left">第一个订阅状态。</param>
        /// <param name="right">第二个订阅状态。</param>
        /// <returns>如果值不相等，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
        public static bool operator !=(PubSubSubscriptionState? left, PubSubSubscriptionState? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("PubSubSubscriptionState:SubscriptionId={0},StreamId={1},Consumer={2}.",
                SubscriptionId, Stream, Consumer);
        }

        /// <summary>
        /// 将订阅标记为故障状态。
        /// </summary>
        public void Fault()
        {
            state = SubscriptionStates.Faulted;
        }

        void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
        {
            if (SubscriptionId is null
                || string.IsNullOrWhiteSpace(Stream.ProviderName)
                || Stream.StreamId == default
                || Consumer.IsDefault
                || !Enum.IsDefined(state))
            {
                throw new System.Text.Json.JsonException($"Could not deserialize {nameof(PubSubSubscriptionState)}.");
            }
        }
    }
}
