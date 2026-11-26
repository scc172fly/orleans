using System;
using System.Collections.Immutable;

namespace Orleans.Runtime
{
    /// <summary>
    /// 代表分组成员简况和前一简况的变动。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class ClusterMembershipUpdate
    {
        /// <summary>
        /// 初始化 <see cref="ClusterMembershipUpdate"/> 类的新实例。
        /// </summary>
        /// <param name="snapshot">快照,快照,我给你的</param>
        /// <param name="changes">更改。</param>
        public ClusterMembershipUpdate(ClusterMembershipSnapshot snapshot, ImmutableArray<ClusterMember> changes)
        {
            this.Snapshot = snapshot;
            this.Changes = changes;
        }

        /// <summary>
        /// 获得一个值,以显示此实例是否有变化。
        /// </summary>
        /// <value><see langword="true"/> 如果这种情况有变化;否则, <see langword="false"/>。</value>
        public bool HasChanges => !this.Changes.IsDefaultOrEmpty;

        /// <summary>
        /// 换了零钱
        /// </summary>
        /// <value>更改。</value>
        [Id(0)]
        public ImmutableArray<ClusterMember> Changes { get; }

        /// <summary>
        /// 拍到快照了 拍到照片了 拍到了
        /// </summary>
        /// <value>快照,快照,我给你的</value>
        [Id(1)]
        public ClusterMembershipSnapshot Snapshot { get; }
    }
}
