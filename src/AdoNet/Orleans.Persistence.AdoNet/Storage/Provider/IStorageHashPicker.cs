using Orleans.Runtime;
using System.Collections.Generic;

namespace Orleans.Storage
{
    /// <summary>
    /// 选择从提供的散列函数中选择的拾取器。 提供更新或更改内置和定制操作的散列功能的快速性。
    /// </summary>
    /// <remarks>返回的散列必须是线条安全或独一实例。</remarks>
    public interface IStorageHasherPicker
    {
        /// <summary>
        /// 保存到此拾取器的散列函数 。
        /// </summary>
        ICollection<IHasher> HashProviders { get; }

        /// <summary>使用给定参数选择散列。</summary>
        /// <param name="serviceId">当前服务的编号。</param>
        /// <param name="storageProviderInstanceName">请求的存储提供者。</param>
        /// <param name="grainType">类型 grain.</param>
        /// <param name="grainId">下 grain ID.</param>
        /// <param name="grainState">下 grain 状态。</param>
        /// <param name="tag">储存参数可用于“带外”合同的可选标记参数。</param>
        /// <returns>A 连续播放器或 <em>无效</em> 如果找不到匹配, 则无法找到 。</returns>
        IHasher? PickHasher<T>(string serviceId, string storageProviderInstanceName, string grainType, GrainId grainId, IGrainState<T> grainState, string? tag = null);
    }
}
