using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于公开集群清单的内部接口。
    /// </summary>
    internal interface IClusterManifestSystemTarget : ISystemTarget
    {
        /// <summary>
        /// 获取当前集群清单。
        /// </summary>
        /// <param name="cancellationToken">用于取消请求的令牌。</param>
        /// <returns>当前集群清单。</returns>
        [Alias("40D39F85")]
        ValueTask<ClusterManifest> GetClusterManifest(CancellationToken cancellationToken = default);

        /// <summary>
        /// 如果集群清单比提供的 <paramref name="previousVersion"/> 更新，则获取更新后的集群清单。
        /// </summary>
        /// <param name="previousVersion">最近一次观察到的清单版本。</param>
        /// <param name="cancellationToken">用于取消请求的令牌。</param>
        /// <returns>当前集群清单；如果不比提供的版本更新，则为 <see langword="null"/>。</returns>
        [Alias("4EFCA109")]
        ValueTask<ClusterManifestUpdate?> GetClusterManifestUpdate(MajorMinorVersion previousVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前集群清单的哈希摘要。
        /// </summary>
        /// <param name="cancellationToken">用于取消请求的令牌。</param>
        /// <returns>当前集群清单的哈希摘要。</returns>
        [Alias("25AE6E4A")]
        ValueTask<ClusterManifestHashSummary> GetClusterManifestHashSummary(CancellationToken cancellationToken);

        /// <summary>
        /// 获取本地 Silo 清单的哈希值。
        /// </summary>
        /// <param name="cancellationToken">用于取消请求的令牌。</param>
        /// <returns>本地 Silo 清单的哈希值。</returns>
        [Alias("3D9B7FE6")]
        ValueTask<ManifestHash> GetSiloManifestHash(CancellationToken cancellationToken);

        /// <summary>
        /// 如果提供的哈希值与本地 Silo 清单匹配，则获取该清单。
        /// </summary>
        /// <param name="hash">预期的清单哈希值。</param>
        /// <param name="cancellationToken">用于取消请求的令牌。</param>
        /// <returns>本地 Silo 清单；如果哈希值不匹配，则为 <see langword="null"/>。</returns>
        [Alias("93B8854F")]
        ValueTask<GrainManifest?> GetSiloManifestByHash(ManifestHash hash, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 使用规范化内容的哈希值标识清单。
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
    /// 表示集群清单的哈希摘要。
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
        /// 获取集群清单版本。
        /// </summary>
        [Id(0)]
        public MajorMinorVersion Version { get; }

        /// <summary>
        /// 获取每个 Silo 的清单哈希值。
        /// </summary>
        [Id(1)]
        public ImmutableDictionary<SiloAddress, ManifestHash> SiloManifestHashes { get; }
    }

    /// <summary>
    /// 表示集群清单的一次更新。
    /// </summary>
    [GenerateSerializer, Immutable]
    public class ClusterManifestUpdate
    {
        /// <summary>
        /// 初始化 <see cref="ClusterManifestUpdate"/> 类的新实例。
        /// </summary>
        /// <param name="manifestVersion">集群清单版本。</param>
        /// <param name="siloManifests">集群中各个 Silo 的清单。</param>
        /// <param name="includesAllActiveServers">
        /// 指示 <paramref name="siloManifests"/> 是否包含所有活动服务器的值。
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
        /// 获取此实例的版本。
        /// </summary>
        [Id(0)]
        public MajorMinorVersion Version { get; }

        /// <summary>
        /// 获取集群中各个 Silo 的清单。
        /// </summary>
        [Id(1)]
        public ImmutableDictionary<SiloAddress, GrainManifest> SiloManifests { get; }

        /// <summary>
        /// 获取指示本次更新是否包含所有活动服务器的值。
        /// </summary>
        [Id(2)]
        public bool IncludesAllActiveServers { get; }
    }
}
