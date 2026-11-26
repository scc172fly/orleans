using System;
using Newtonsoft.Json;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 代表订阅流的酒吧/分注册国。
    /// </summary>
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    [GenerateSerializer]
    public sealed class PubSubSubscriptionState : IEquatable<PubSubSubscriptionState?>, System.Text.Json.Serialization.IJsonOnDeserialized
    {
        /// <summary>
        /// 确定溪流订阅的生命周期状态。
        /// </summary>
        public enum SubscriptionStates
        {
            /// <summary>
            /// 订阅活动进行中。
            /// </summary>
            Active,

            /// <summary>
            /// 订阅失败了。
            /// </summary>
            Faulted,
        }

        // 重要!
        // 这些字段必须是非公开的, 无法被读取, 以便 JSonSerialization 工作!
        // 如果将其中任何一项修改为只读, 则执行可实现的 ISeal
        /// <summary>
        /// 订阅标识符 。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("subscriptionId")]
        [Id(0)]
        public GuidId SubscriptionId;

        /// <summary>
        /// 订阅流的合格识别码。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("stream")]
        [Id(1)]
        public QualifiedStreamId Stream;

        /// <summary>
        /// 消耗流的 grain 标识符。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("consumer")]
        [Id(2)]
        public GrainId Consumer; // 字段需要公开类型, 否则我们不会为该类生成 Orleans 序列器。

        /// <summary>
        /// 与订阅有关的序列式过滤数据。
        /// </summary>
        [JsonProperty]
        [System.Text.Json.Serialization.JsonInclude]
        [System.Text.Json.Serialization.JsonPropertyName("filterData")]
        [Id(3)]
        public string? FilterData; // 串联性真菌信息

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
        /// 获得一个值,显示订阅是否错误。
        /// </summary>
        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFaulted { get { return state == SubscriptionStates.Faulted; } }

        // 建造者必须公开工作才能 JSonSerialization !
        // 如果将其改为非公开,则可以实施
        /// <summary>
        /// 启动活跃流订阅登记。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="streamId">合格的流识别符号。</param>
        /// <param name="streamConsumer">消耗流的 grain 标识符。</param>
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
        /// 初始化流订阅登记。
        /// </summary>
        /// <param name="subscriptionId">订阅标识符。</param>
        /// <param name="stream">合格的流识别符号。</param>
        /// <param name="consumer">消耗流的 grain 标识符。</param>
        /// <param name="filterData">与订阅有关的序列式过滤数据。</param>
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
        /// 设置与订阅相关的序列过滤数据。
        /// </summary>
        /// <param name="filterData">序列式过滤数据。</param>
        public void AddFilter(string? filterData)
        {
            this.FilterData = filterData;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            // 注:不能使用 PubSubSubscriptionState 上的“作为”运算符,因为它是一个支架。
            return obj is PubSubSubscriptionState && Equals((PubSubSubscriptionState)obj);
        }

        /// <inheritdoc/>
        public bool Equals(PubSubSubscriptionState? other)
        {
            if ((object?)other == null)
                return false;
            // 注: PubSubSubscriptionState 是支架,所以“其他”永远不能无效。
            return Equals(other.SubscriptionId);
        }

        /// <summary>
        /// 确定此订阅是否拥有指定的标识符 。
        /// </summary>
        /// <param name="subscriptionId">要比较的订阅识别码 。</param>
        /// <returns><see langword="true"/> 如果识别符号相等;否则, <see langword="false"/>。</returns>
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
        /// 与两个订阅国按订阅标识符分列的平等点比较。
        /// </summary>
        /// <param name="left">第一个订阅状态。</param>
        /// <param name="right">第二个订阅国。</param>
        /// <returns>
        /// <see langword="true"/> ,如果两个数值均为 <see langword="null"/> 或具有相同的订阅标识符;
        /// 否则, <see langword="false"/>.
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
        /// 与两个订阅国相比,按订阅标识符分列的不平等状况。
        /// </summary>
        /// <param name="left">第一个订阅状态。</param>
        /// <param name="right">第二个订阅国。</param>
        /// <returns><see langword="true"/> ,如果数值不相等;否则, <see langword="false"/>。</returns>
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
        /// 标记订阅为错误 。
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
