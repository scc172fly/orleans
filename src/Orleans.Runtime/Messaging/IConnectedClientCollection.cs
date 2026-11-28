using System.Collections.Generic;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示当前连接到此网关的客户端集合。
    /// </summary>
    internal interface IConnectedClientCollection
    {
        /// <summary>
        /// 集合的单调递增版本，可用于变更通知。
        /// </summary>
        long Version { get; }

        /// <summary>
        /// 获取集合中的客户端数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 获取已连接客户端的标识集合。
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
