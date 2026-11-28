using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Storage
{
    /// <summary>
    /// 与 Orleans v3 兼容的哈希算法选择器实现，适用于 Orleans v3 到 v7+ 的迁移场景。
    /// </summary>
    public class Orleans3CompatibleStorageHashPicker : IStorageHasherPicker
    {
        private readonly Orleans3CompatibleHasher _nonStringHasher;

        /// <summary>
        /// <see cref="IStorageHasherPicker.HashProviders"/>.
        /// </summary>
        public ICollection<IHasher> HashProviders { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public Orleans3CompatibleStorageHashPicker()
        {
            _nonStringHasher = new();
            HashProviders = [_nonStringHasher];
        }

        /// <summary>
        /// <see cref="IStorageHasherPicker.PickHasher{T}"/>.
        /// </summary>
        public IHasher PickHasher<T>(
            string serviceId,
            string storageProviderInstanceName,
            string grainType,
            GrainId grainId,
            IGrainState<T> grainState,
            string? tag = null)
        {
            // Orleans v3 中的纯字符串 Grain 键具有特殊行为。
            if (grainId.TryGetIntegerKey(out _, out _) || grainId.TryGetGuidKey(out _, out _))
                return _nonStringHasher;

            // 无法缓存哈希算法实例：其行为感知内容，详情参见哈希算法实现。
            return new Orleans3CompatibleStringKeyHasher(_nonStringHasher, grainType);
        }
    }
}
