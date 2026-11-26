using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.EventSourcing.Common
{
    /// <summary>
    /// 日志查看调整器发送到其它 
    /// 组群, 在更新日志后。 所有子类都必须可以序列 。
    /// </summary>
    public interface INotificationMessage : ILogConsistencyProtocolMessage
    {
        ///<summary>版本号。</summary>
        int Version { get; }

        // 日志一致性提供商可以将此分类子分类以添加更多信息
        // 例如,所附加的日志条目或视图
    }

    /// <summary>仅包含版本的简单通知信息。</summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class VersionNotificationMessage : INotificationMessage
    {
        /// <inheritdoc/>
        [Id(0)]
        public int Version { get; set; }
    }


    /// <summary>含有一批通知信息的通知电文。</summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class BatchedNotificationMessage : INotificationMessage
    {
        /// <summary>本批所载通知信息。</summary>
        [Id(0)]
        public List<INotificationMessage> Notifications { get; set; } = null!;

        /// <summary>版本号 - 对于某批,这是包含的最大版本。</summary>
        public int Version
        {
            get
            {
                return Notifications.Aggregate(0, (v, m) => Math.Max(v, m.Version));
            }
        }
    }
}
