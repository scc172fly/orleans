using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Orleans.Runtime;

namespace Orleans.Streams
{
    [Serializable]
    [JsonObject(MemberSerialization.OptIn)]
    [GenerateSerializer]
    internal sealed class PubSubPublisherState : IEquatable<PubSubPublisherState?>
    {
        public static MembershipVersion UnknownMembershipVersion => default;

        // 重要！！！！！
        // 为使 JsonSerialization 正常工作，这些字段必须为 public 且不能为 readonly！
        // 如果将其中任何字段改为 readonly，则需实现 ISerializable。
        [JsonProperty]
        [Id(0)]
        public QualifiedStreamId Stream;

        [JsonProperty]
        [Id(1)]
        public GrainId Producer; // 此字段的类型必须为公开类型，否则不会为该类生成 Orleans 序列化器。

        [JsonProperty]
        [Id(2)]
        // 旧状态由于缺少此字段而具有默认值。MembershipVersion 不参与
        // 相等性比较，以便重新注册时可以就地推进其版本。
        public MembershipVersion MembershipVersion;

        // 为使 JsonSerialization 正常工作，此构造函数必须为 public！
        // 如果将其改为非 public，则需实现 ISerializable。
        public PubSubPublisherState(QualifiedStreamId streamId, GrainId streamProducer)
        {
            Stream = streamId;
            Producer = streamProducer;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            // 注意：不能对 PubSubPublisherState 使用 'as' 运算符，因为它是类。（译注：类是引用类型，实际可以使用 as 运算符；原文有误。）
            return obj is PubSubPublisherState && Equals((PubSubPublisherState)obj);
        }
        public bool Equals(PubSubPublisherState? other)
        {
            return Equals(other!.Stream, other.Producer); // 保留旧版空引用解引用行为。
        }
        public bool Equals(QualifiedStreamId streamId, GrainId streamProducer)
        {
            if (Stream == default) return false;
            return Stream.Equals(streamId) && Producer.Equals(streamProducer);
        }

        public static bool operator ==(PubSubPublisherState? left, PubSubPublisherState? right)
        {
            return left!.Equals(right); // 保留旧版空引用解引用行为。
        }
        public static bool operator !=(PubSubPublisherState? left, PubSubPublisherState? right)
        {
            return !left!.Equals(right); // 保留旧版空引用解引用行为。
        }
        public override int GetHashCode() => HashCode.Combine(Stream, Producer);

        public override string ToString()
        {
            return string.Format(
                "PubSubPublisherState:StreamId={0},Producer={1},MembershipVersion={2}.",
                Stream,
                Producer,
                MembershipVersion);
        }
    }
}
