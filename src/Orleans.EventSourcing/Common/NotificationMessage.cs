using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 日志视图适配器在更新日志后发送给其他集群的
    /// 通知消息的基类。所有子类都必须可序列化。
    /// </summary>
    public interface INotificationMessage : ILogConsistencyProtocolMessage
    {
        /// <summary>版本号。</summary>
        int Version { get; }

        // 日志一致性提供程序可通过派生此类来添加更多信息，
        // 例如追加的日志条目或视图。
    }

    /// <summary>仅包含版本号的简单通知消息。</summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class VersionNotificationMessage : INotificationMessage
    {
        /// <inheritdoc/>
        [Id(0)]
        public int Version { get; set; }
    }


    /// <summary>包含一批通知消息的通知消息。</summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class BatchedNotificationMessage : INotificationMessage
    {
        /// <summary>该批次包含的通知消息。</summary>
        [Id(0)]
        public List<INotificationMessage> Notifications { get; set; } = null!;

        /// <summary>版本号；对于一个批次，此值是其中最大的版本号。</summary>
        public int Version
        {
            get
            {
                return Notifications.Aggregate(0, (v, m) => Math.Max(v, m.Version));
            }
        }
    }
}
