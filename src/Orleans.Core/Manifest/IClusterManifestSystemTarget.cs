using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 显示集束清单的内部界面。
    /// </summary>
    internal interface IClusterManifestSystemTarget : ISystemTarget
    {
        /// <summary>
        /// 获取当前集的显示器 。
        /// </summary>
        /// <param name="cancellationToken">取消请求所用的标记。</param>
        /// <returns>当前组群显示。</returns>
        [Alias("40D39F85")]
        ValueTask<ClusterManifest> GetClusterManifest(CancellationToken cancellationToken = default);

        /// <summary>
        /// 如果数据组的编号比提供的数据组 <paramref name="previousVersion"/>更新,则获得更新后的分类组合清单。
        /// </summary>
        /// <param name="previousVersion">最后观察到的明显版本。</param>
        /// <param name="cancellationToken">取消请求所用的标记。</param>
        /// <returns>当前组群的显示值, 或如果其不比所提供的版本更新的话 <see langword="null"/> 。</returns>
        [Alias("4EFCA109")]
        ValueTask<ClusterManifestUpdate?> GetClusterManifestUpdate(MajorMinorVersion previousVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前组群列表的散列摘要 。
        /// </summary>
        /// <param name="cancellationToken">取消请求所用的标记。</param>
        /// <returns>现有组群散列表摘要。</returns>
        [Alias("25AE6E4A")]
        ValueTask<ClusterManifestHashSummary> GetClusterManifestHashSummary(CancellationToken cancellationToken);

        /// <summary>
        /// 获得当地大麻 silo 数据。
        /// </summary>
        /// <param name="cancellationToken">取消请求所用的标记。</param>
        /// <returns>本地 silo 清单的散列。</returns>
        [Alias("3D9B7FE6")]
        ValueTask<ManifestHash> GetSiloManifestHash(CancellationToken cancellationToken);

        /// <summary>
        /// 如果提供的大麻与当地 silo 清单相符,则获取该清单。
        /// </summary>
        /// <param name="hash">预期的散列。</param>
        /// <param name="cancellationToken">取消请求所用的标记。</param>
        /// <returns>本地 silo 清单,或 <see langword="null"/> 如果散列不符。</returns>
        [Alias("93B8854F")]
        ValueTask<GrainManifest?> GetSiloManifestByHash(ManifestHash hash, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 以其卡通成分散列物来标明一个表象。
    /// </summary>
    [GenerateSerializer, Immutable]
    internal readonly struct ManifestHash : System.IEquatable<ManifestHash>
    {
        public ManifestHash(string value)
        {
            Value = value;
        }

        [Id(0)]
        public string Value { get; }

        public bool Equals(ManifestHash other) => string.Equals(Value, other.Value, System.StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ManifestHash other && Equals(other);

        public override int GetHashCode() => System.StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ManifestHash left, ManifestHash right) => left.Equals(right);

        public static bool operator !=(ManifestHash left, ManifestHash right) => !left.Equals(right);
    }

    /// <summary>
    /// 代表集束货单的散列摘要。
    /// </summary>
    [GenerateSerializer, Immutable]
    internal sealed class ClusterManifestHashSummary
    {
        public ClusterManifestHashSummary(
            MajorMinorVersion version,
            Dictionary<SiloAddress, ManifestHash> siloManifestHashes)
        {
            Version = version;
            SiloManifestHashes = siloManifestHashes.ToImmutableDictionary();
        }

        /// <summary>
        /// 获得集束表的表达式版本。
        /// </summary>
        [Id(0)]
        public MajorMinorVersion Version { get; }

        /// <summary>
        /// 获得每个文件的明显散列 silo.
        /// </summary>
        [Id(1)]
        public ImmutableDictionary<SiloAddress, ManifestHash> SiloManifestHashes { get; }
    }

    /// <summary>
    /// 代表对分组货单的最新说明。
    /// </summary>
    [GenerateSerializer, Immutable]
    public class ClusterManifestUpdate
    {
        /// <summary>
        /// 初始化 <see cref="ClusterManifestUpdate"/> 类的新实例。
        /// </summary>
        /// <param name="manifestVersion">集束货单版本。</param>
        /// <param name="siloManifests">每一 silo 组内。</param>
        /// <param name="includesAllActiveServers">
        /// 表示 <paramref name="siloManifests"/> 是否包含每个活动服务器的值。
        /// </param>
        public ClusterManifestUpdate(
            MajorMinorVersion manifestVersion,
            ImmutableDictionary<SiloAddress, GrainManifest> siloManifests,
            bool includesAllActiveServers)
        {
            Version = manifestVersion;
            SiloManifests = siloManifests;
            IncludesAllActiveServers = includesAllActiveServers;
        }

        /// <summary>
        /// 获取这个例子的版本 。
        /// </summary>
        [Id(0)]
        public MajorMinorVersion Version { get; }

        /// <summary>
        /// 获取集中每个 silo 的货单。
        /// </summary>
        [Id(1)]
        public ImmutableDictionary<SiloAddress, GrainManifest> SiloManifests { get; }

        /// <summary>
        /// 获得一个值, 显示此更新是否包含所有活动服务器 。
        /// </summary>
        [Id(2)]
        public bool IncludesAllActiveServers { get; }
    }
}
