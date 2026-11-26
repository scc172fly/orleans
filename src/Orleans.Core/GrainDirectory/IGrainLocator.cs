using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.GrainDirectory
{
    /// <summary>
    /// 用于定位集组中的 Grain 激活
    /// </summary>
    public interface IGrainLocator
    {
        /// <summary>
        /// 在适当的 grain 目录中登记所提供的地址。
        /// </summary>
        /// <param name="address">登记地址。</param>
        /// <param name="previousRegistration">先前的登记将替换,如果知道的话。</param>
        /// <returns>下 grain 此呼叫后立即在目录中注册的地址。</returns>
        Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousRegistration);

        /// <summary>
        /// 由目录中设置 grain 地址。
        /// </summary>
        /// <param name="address">解除登记地址。</param>
        /// <param name="cause">取消登记的原因。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        Task Unregister(GrainAddress address, UnregistrationCause cause);

        /// <summary>
        /// 查找 grain的相应地址。
        /// </summary>
        /// <param name="grainId">下 grain id. 数据。</param>
        /// <returns>与指定地址对应的地址 grain id,或 <see langword="null"/> 如果 grain 目前尚未登记。</returns>
        ValueTask<GrainAddress?> Lookup(GrainId grainId);

        /// <summary>
        /// 以 grain 安置决定或已知激活地址更新缓存。
        /// </summary>
        /// <param name="grainId"> grain 标识符。</param>
        /// <param name="siloAddress">下 silo 可能作为东道国的 grain.</param>
        void UpdateCache(GrainId grainId, SiloAddress siloAddress);

        /// <summary>
        /// 无效与所提供的 grain id 相关的任何查找缓存条目 。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        void InvalidateCache(GrainId grainId);

        /// <summary>
        /// 从查找缓存中删除指定的地址 。
        /// </summary>
        /// <param name="address">
        /// grain 地址无效。
        /// </param>
        void InvalidateCache(GrainAddress address);

        /// <summary>
        /// 试图寻找 grain 提供地址的地址 grain id,没有从事非同步的工作。
        /// 执行者可查询地方缓存或权威的地方目录分区。
        /// </summary>
        /// <param name="grainId">下 grain 需要找到的ID。</param>
        /// <param name="address">由此结果 grain 地址, 如果找到的话, 或者 <see langword="null"/> 找不到的话。</param>
        /// <returns>值,表示是否找到有效条目。</returns>
        bool TryLookupInCache(GrainId grainId, [NotNullWhen(true)] out GrainAddress? address);
    }
}
