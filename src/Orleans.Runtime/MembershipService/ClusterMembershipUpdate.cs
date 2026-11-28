using System;
using System.Collections.Immutable;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示集群成员资格快照及相对于上一份快照的变更。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public sealed class ClusterMembershipUpdate
    {
        /// <summary>
        /// 初始化 <see cref="ClusterMembershipUpdate"/> 类的新实例。
        /// </summary>
        /// <param name="snapshot">快照。</param>
        /// <param name="changes">变更。</param>
        public ClusterMembershipUpdate(ClusterMembershipSnapshot snapshot, ImmutableArray<ClusterMember> changes)
        {
            this.Snapshot = snapshot;
            this.Changes = changes;
        }

        /// <summary>
        /// 获取指示此实例是否包含变更的值。
        /// </summary>
        /// <value>如果此实例包含变更，则为 <see langword="true"/>；否则为 <see langword="false"/>。</value>
        public bool HasChanges => !this.Changes.IsDefaultOrEmpty;

        /// <summary>
        /// 获取变更。
        /// </summary>
        /// <value>变更。</value>
        [Id(0)]
        public ImmutableArray<ClusterMember> Changes { get; }

        /// <summary>
        /// 获取快照。
        /// </summary>
        /// <value>快照。</value>
        [Id(1)]
        public ClusterMembershipSnapshot Snapshot { get; }
    }
}
