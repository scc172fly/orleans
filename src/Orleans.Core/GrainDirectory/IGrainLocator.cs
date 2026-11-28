using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// 用于在集群中定位 Grain 激活实例。
    /// </summary>
    public interface IGrainLocator
    {
        /// <summary>
        /// 在相应的 Grain 目录中注册提供的地址。
        /// </summary>
        /// <param name="address">要注册的地址。</param>
        /// <param name="previousRegistration">要替换的先前注册记录（如果已知）。</param>
        /// <returns>此次调用刚完成时在目录中注册的 Grain 地址。</returns>
        Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousRegistration);

        /// <summary>
        /// 从目录中注销 Grain 地址。
        /// </summary>
        /// <param name="address">要注销的地址。</param>
        /// <param name="cause">注销的原因。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        Task Unregister(GrainAddress address, UnregistrationCause cause);

        /// <summary>
        /// 查找 Grain 对应的地址。
        /// </summary>
        /// <param name="grainId">Grain ID。</param>
        /// <returns>与指定 Grain ID 对应的地址；如果 Grain 当前未注册，则为 <see langword="null"/>。</returns>
        ValueTask<GrainAddress?> Lookup(GrainId grainId);

        /// <summary>
        /// 使用 Grain 放置决策或已知激活实例地址更新缓存。
        /// </summary>
        /// <param name="grainId">Grain 标识符。</param>
        /// <param name="siloAddress">可能承载该 Grain 的 Silo。</param>
        void UpdateCache(GrainId grainId, SiloAddress siloAddress);

        /// <summary>
        /// 使与所提供 Grain ID 关联的查找缓存条目失效。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        void InvalidateCache(GrainId grainId);

        /// <summary>
        /// 从查找缓存中移除指定地址。
        /// </summary>
        /// <param name="address">
        /// 要使其失效的 Grain 地址。
        /// </param>
        void InvalidateCache(GrainAddress address);

        /// <summary>
        /// 尝试查找与所提供 Grain ID 对应的地址，不执行异步工作。
        /// 实现可以查询本地缓存或权威的本地目录分区。
        /// </summary>
        /// <param name="grainId">要查找的 Grain ID。</param>
        /// <param name="address">查找成功时为所得 Grain 地址，否则为 <see langword="null"/>。</param>
        /// <returns>指示是否找到有效条目的值。</returns>
        bool TryLookupInCache(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address);
    }
}
