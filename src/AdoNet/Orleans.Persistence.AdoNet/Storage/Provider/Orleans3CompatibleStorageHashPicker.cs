using System.Collections.Generic;
using Orleans.Runtime;

namespace Orleans.Storage
{
    /// <summary>
    /// Orleans v3 兼容的散射采集器执行 Orleans v3 - > v7+迁移情景。
    /// </summary>
    public class Orleans3CompatibleStorageHashPicker : IStorageHasherPicker
    {
        private readonly Orleans3CompatibleHasher _nonStringHasher;

        /// <summary>
        /// <see cref="IStorageHasherPicker.HashProviders"/>.
        /// </summary>
        public ICollection<IHasher> HashProviders { get; }

        /// <summary>
        /// 建筑师
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
            // 仅字符串 grain 键在 Orleans v3 有特殊行为
            if (grainId.TryGetIntegerKey(out _, out _) || grainId.TryGetGuidKey(out _, out _))
                return _nonStringHasher;

            // 无法隐藏有误的事例:内容意识行为,详情请见hashher impact
            return new Orleans3CompatibleStringKeyHasher(_nonStringHasher, grainType);
        }
    }
}
