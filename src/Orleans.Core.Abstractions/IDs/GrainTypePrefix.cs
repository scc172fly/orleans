using System;
using System.Text;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="GrainType"/>的前缀和相应的辅助工具方法。
    /// </summary>
    public static class GrainTypePrefix
    {
        /// <summary>
        /// 系统类型前缀 。
        /// </summary>
        public const string SystemPrefix = "sys.";

        /// <summary>
        /// 系统目标的前缀 。
        /// </summary>
        public const string SystemTargetPrefix = SystemPrefix + "svc.";

        /// <summary>
        /// 代表 <see cref="SystemTargetPrefix" />的跨度。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> SystemTargetPrefixBytes = Encoding.UTF8.GetBytes(SystemTargetPrefix);

        /// <summary>
        /// grain 服务类型的前缀。
        /// </summary>
        public const string GrainServicePrefix = SystemTargetPrefix + "user.";

        /// <summary>
        /// 代表 <see cref="ClientPrefix" />的跨度。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> GrainServicePrefixBytes = Encoding.UTF8.GetBytes(GrainServicePrefix);

        /// <summary>
        /// 客户端的前缀 。
        /// </summary>
        public const string ClientPrefix = SystemPrefix + "client";

        /// <summary>
        /// 代表 <see cref="ClientPrefix" />的跨度。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> ClientPrefixBytes = Encoding.UTF8.GetBytes(ClientPrefix);

        /// <summary>
        /// 用于代表 grain 客户端的前缀。
        /// </summary>
        public static readonly GrainType ClientGrainType = GrainType.Create(ClientPrefix);

        /// <summary>
        /// 遗留问题的前缀 grains。
        /// </summary>
        public const string LegacyGrainPrefix = SystemPrefix + "grain.v1.";

        /// <summary>
        /// 代表 <see cref="LegacyGrainPrefixBytes" />的跨度。
        /// </summary>
        public static readonly ReadOnlyMemory<byte> LegacyGrainPrefixBytes = Encoding.UTF8.GetBytes(LegacyGrainPrefix);

        /// <summary>
        /// 返回 <see langword="true"/> 如果该类型是客户端, <see langword="false"/> 情况并非如此。
        /// </summary>
        /// <param name="type"> grain 类型。</param>
        /// <returns><see langword="true"/> 如果该类型是客户端, <see langword="false"/> 如果不是。</returns>
        public static bool IsClient(this in GrainType type) => type.AsSpan().StartsWith(ClientPrefixBytes.Span);

        /// <summary>
        /// 返回 <see langword="true"/> 如果该类型是系统目标, <see langword="false"/> 情况并非如此。
        /// </summary>
        /// <param name="type"> grain 类型。</param>
        /// <returns><see langword="true"/> 如果该类型是系统目标,则 <see langword="false"/> 。</returns>
        public static bool IsSystemTarget(this in GrainType type) => type.AsSpan().StartsWith(SystemTargetPrefixBytes.Span);

        /// <summary>
        /// 返回 <see langword="true"/> 如果类型是遗产 grain, <see langword="false"/> 情况并非如此。
        /// </summary>
        /// <param name="type"> grain 类型。</param>
        /// <returns><see langword="true"/> 如果类型是遗产 grain, <see langword="false"/> 情况并非如此。</returns>
        public static bool IsLegacyGrain(this in GrainType type) => type.AsSpan().StartsWith(LegacyGrainPrefixBytes.Span);

        /// <summary>
        /// 返回 <see langword="true"/> 如果类型是 a grain 服务, <see langword="false"/> 情况并非如此。
        /// </summary>
        /// <param name="type"> grain 类型。</param>
        /// <returns><see langword="true"/> 如果该类型是 grain 服务,如果不是,则 <see langword="false"/> 。</returns>
        public static bool IsGrainService(this in GrainType type) => type.AsSpan().StartsWith(GrainServicePrefixBytes.Span);

        /// <summary>
        /// 返回 <see langword="true"/> 如果该身份代表客户, <see langword="false"/> 情况并非如此。
        /// </summary>
        /// <param name="id">下 grain id. 数据。</param>
        /// <returns><see langword="true"/> 如果该类型是客户端, <see langword="false"/> 如果不是。</returns>
        public static bool IsClient(this in GrainId id) => id.Type.IsClient();

        /// <summary>
        /// 返回 <see langword="true"/> 如果该ID代表系统目标, <see langword="false"/> 情况并非如此。
        /// </summary>
        /// <param name="id">下 grain id. 数据。</param>
        /// <returns><see langword="true"/> 如果该类型是系统目标,则 <see langword="false"/> 。</returns>
        public static bool IsSystemTarget(this in GrainId id) => id.Type.IsSystemTarget();
    }
}
