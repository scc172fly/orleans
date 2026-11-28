using Orleans.Runtime;
using System.Collections.Generic;

namespace Orleans.Storage
{
    /// <summary>
    /// 用于从提供的哈希函数中进行选择的选择器，可以灵活地更新或更改内置操作及自定义操作的哈希功能。
    /// </summary>
    /// <remarks>返回的哈希算法必须是线程安全的，或使用独立实例。</remarks>
    public interface IStorageHasherPicker
    {
        /// <summary>
        /// 保存在此选择器中的哈希函数。
        /// </summary>
        ICollection<IHasher> HashProviders { get; }

        /// <summary>使用给定参数选择哈希算法。</summary>
        /// <param name="serviceId">当前服务的 ID。</param>
        /// <param name="storageProviderInstanceName">发出请求的存储提供程序。</param>
        /// <param name="grainType">Grain 类型。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">Grain 状态。</param>
        /// <param name="tag">可选的标记参数，存储参数可能将其用于带外契约。</param>
        /// <returns>序列化器；如果未找到匹配项，则为 <em>null</em>。</returns>
        IHasher? PickHasher<T>(string serviceId, string storageProviderInstanceName, string grainType, GrainId grainId, IGrainState<T> grainState, string? tag = null);
    }
}
