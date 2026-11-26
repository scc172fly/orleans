using System.Collections.Generic;

namespace Orleans.Runtime
{
    /// <summary>
    /// 代表目前与该网关连接的客户集。
    /// </summary>
    internal interface IConnectedClientCollection
    {
        /// <summary>
        /// 单质增加的收藏版本,可用于更改通知。
        /// </summary>
        long Version { get; }

        /// <summary>
        /// 获得收藏中的客户数。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 收集相关客户的识别码。
        /// </summary>
        List<GrainId> GetConnectedClientIds();
    }

    internal sealed class EmptyConnectedClientCollection : IConnectedClientCollection
    {
        public long Version => 0;

        public int Count => 0;

        public List<GrainId> GetConnectedClientIds() => new List<GrainId>(0);
    }
}
