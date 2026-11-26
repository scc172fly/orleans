using System;
using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 流体特性包含公共流信息,用于独特识别流。
    /// 流体特性只是每个流体提供商独有的。
    /// </summary>
    /// <remarks>
    /// 在可能的情况下使用 <see cref="StreamId"/> 。
    /// </remarks>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class StreamIdentity : IStreamIdentity
    {
        /// <summary>
        /// 初始化 <see cref="StreamIdentity"/> 类的新实例。
        /// </summary>
        /// <param name="streamGuid">流流独有标识符。</param>
        /// <param name="streamNamespace">流命名空间。</param>
        public StreamIdentity(Guid streamGuid, string? streamNamespace)
        {
            Guid = streamGuid;
            Namespace = streamNamespace;
        }

        /// <summary>
        /// 获取溪流识别码。
        /// </summary>
        [Id(0)]
        public Guid Guid { get; }

        /// <summary>
        /// 获取流命名空间 。
        /// </summary>
        [Id(1)]
        public string? Namespace { get; }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is StreamIdentity identity && this.Guid.Equals(identity.Guid) && this.Namespace == identity.Namespace;

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Guid, Namespace);
    }
}
