using System.Collections.Generic;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 提供对集群清单的访问。
    /// </summary>
    /// <seealso cref="ClusterManifest"/>
    public interface IClusterManifestProvider
    {
        /// <summary>
        /// 获取当前集群清单。
        /// </summary>
        ClusterManifest Current { get; }

        /// <summary>
        /// 获取集群清单的更新流。
        /// </summary>
        IAsyncEnumerable<ClusterManifest> Updates { get; }

        /// <summary>
        /// 获取本地 Grain 清单。
        /// </summary>
        GrainManifest LocalGrainManifest { get; }
    }
}
