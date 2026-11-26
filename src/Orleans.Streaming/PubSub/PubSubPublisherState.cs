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

        // 重要!
        // 这些字段必须是非公开的, 无法被读取, 以便 JsonSerialization 工作!
        // 如果将其中任何一项修改为只读, 则执行可实现的 ISeal
        [JsonProperty]
        [Id(0)]
        public QualifiedStreamId Stream;

        [JsonProperty]
        [Id(1)]
        public GrainId Producer; // 字段需要公开类型, 否则我们不会为该类生成 Orleans 序列器。

        [JsonProperty]
        [Id(2)]
        // 旧状态有默认值, 因为此字段不存在。 MembershipVersion 被排除
        // 重新登记可以推进其落实。
        public MembershipVersion MembershipVersion;

        // 建造者必须公开工作才能 JsonSerialization !
        // 如果将其改为非公开,则可以实施
        public PubSubPublisherState(QualifiedStreamId streamId, GrainId streamProducer)
        {
            Stream = streamId;
            Producer = streamProducer;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            // 注:不能使用 PubSubPublisherState 上的“作为”运算符,因为它是一个类。
            return obj is PubSubPublisherState && Equals((PubSubPublisherState)obj);
        }
        public bool Equals(PubSubPublisherState? other)
        {
            return Equals(other!.Stream, other.Producer); // 保护遗产的无效行为。
        }
        public bool Equals(QualifiedStreamId streamId, GrainId streamProducer)
        {
            if (Stream == default) return false;
            return Stream.Equals(streamId) && Producer.Equals(streamProducer);
        }

        public static bool operator ==(PubSubPublisherState? left, PubSubPublisherState? right)
        {
            return left!.Equals(right); // 保护遗产的无效行为。
        }
        public static bool operator !=(PubSubPublisherState? left, PubSubPublisherState? right)
        {
            return !left!.Equals(right); // 保护遗产的无效行为。
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
