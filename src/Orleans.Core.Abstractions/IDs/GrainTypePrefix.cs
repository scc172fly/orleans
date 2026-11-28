using System;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="GrainType"/> 的前缀及对应的辅助方法。
    /// </summary>
    public static class GrainTypePrefix
    {
        /// <summary>
        /// 系统类型的前缀。
        /// </summary>
        public const string SystemPrefix = "sys.";

        /// <summary>
        /// 系统目标的前缀。
        /// </summary>
        public const string SystemTargetPrefix = SystemPrefix + "svc.";

        /// <summary>
        /// <see cref="SystemTargetPrefix" /> 的 Span 表示形式。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> SystemTargetPrefixBytes = Encoding.UTF8.GetBytes(SystemTargetPrefix);

        /// <summary>
        /// Grain 服务类型的前缀。
        /// </summary>
        public const string GrainServicePrefix = SystemTargetPrefix + "user.";

        /// <summary>
        /// <see cref="ClientPrefix" /> 的 Span 表示形式。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> GrainServicePrefixBytes = Encoding.UTF8.GetBytes(GrainServicePrefix);

        /// <summary>
        /// 客户端的前缀。
        /// </summary>
        public const string ClientPrefix = SystemPrefix + "client";

        /// <summary>
        /// <see cref="ClientPrefix" /> 的 Span 表示形式。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> ClientPrefixBytes = Encoding.UTF8.GetBytes(ClientPrefix);

        /// <summary>
        /// 用于表示 Grain 客户端的前缀。
        /// </summary>
        public static readonly GrainType ClientGrainType = GrainType.Create(ClientPrefix);

        /// <summary>
        /// 旧版 Grain 的前缀。
        /// </summary>
        public const string LegacyGrainPrefix = SystemPrefix + "grain.v1.";

        /// <summary>
        /// <see cref="LegacyGrainPrefixBytes" /> 的 Span 表示形式。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> LegacyGrainPrefixBytes = Encoding.UTF8.GetBytes(LegacyGrainPrefix);

        /// <summary>
        /// 如果类型为客户端，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="type">Grain 类型。</param>
        /// <returns>如果类型为客户端，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool IsClient(this in GrainType type) => type.AsSpan().StartsWith(ClientPrefixBytes.Span);

        /// <summary>
        /// 如果类型为系统目标，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="type">Grain 类型。</param>
        /// <returns>如果类型为系统目标，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool IsSystemTarget(this in GrainType type) => type.AsSpan().StartsWith(SystemTargetPrefixBytes.Span);

        /// <summary>
        /// 如果类型为旧版 Grain，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="type">Grain 类型。</param>
        /// <returns>如果类型为旧版 Grain，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool IsLegacyGrain(this in GrainType type) => type.AsSpan().StartsWith(LegacyGrainPrefixBytes.Span);

        /// <summary>
        /// 如果类型为 Grain 服务，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="type">Grain 类型。</param>
        /// <returns>如果类型为 Grain 服务，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool IsGrainService(this in GrainType type) => type.AsSpan().StartsWith(GrainServicePrefixBytes.Span);

        /// <summary>
        /// 如果 ID 表示客户端，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="id">Grain ID。</param>
        /// <returns>如果类型为客户端，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool IsClient(this in GrainId id) => id.Type.IsClient();

        /// <summary>
        /// 如果 ID 表示系统目标，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="id">Grain ID。</param>
        /// <returns>如果类型为系统目标，则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        public static bool IsSystemTarget(this in GrainId id) => id.Type.IsSystemTarget();
    }
}
