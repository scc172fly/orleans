using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流标识包含唯一标识流所需的公开信息。
    /// 流标识仅在同一个流提供程序内唯一。
    /// </summary>
    /// <remarks>
    /// 尽可能改用 <see cref="StreamId"/>。
    /// </remarks>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class StreamIdentity : IStreamIdentity
    {
        /// <summary>
        /// 初始化 <see cref="StreamIdentity"/> 类的新实例。
        /// </summary>
        /// <param name="streamGuid">流的唯一标识符。</param>
        /// <param name="streamNamespace">流的命名空间。</param>
        public StreamIdentity(Guid streamGuid, string? streamNamespace)
        {
            Guid = streamGuid;
            Namespace = streamNamespace;
        }

        /// <summary>
        /// 获取流标识符。
        /// </summary>
        [Id(0)]
        public Guid Guid { get; }

        /// <summary>
        /// 获取流的命名空间。
        /// </summary>
        [Id(1)]
        public string? Namespace { get; }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is StreamIdentity identity && this.Guid.Equals(identity.Guid) && this.Namespace == identity.Namespace;

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Guid, Namespace);
    }
}
