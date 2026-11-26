using System.Collections.Generic;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 提供进入集束货单的通道。
    /// </summary>
    /// <seealso cref="ClusterManifest"/>
    public interface IClusterManifestProvider
    {
        /// <summary>
        /// 获取当前集的显示器 。
        /// </summary>
        ClusterManifest Current { get; }

        /// <summary>
        /// 获取集束显示更新的流。
        /// </summary>
        IAsyncEnumerable<ClusterManifest> Updates { get; }

        /// <summary>
        /// 获取本地 grain 清单。
        /// </summary>
        GrainManifest LocalGrainManifest { get; }
    }
}
