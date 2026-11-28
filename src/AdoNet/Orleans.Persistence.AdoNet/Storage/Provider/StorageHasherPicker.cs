using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Orleans.Storage
{
    /// <summary>
    /// <see cref="IStorageHasherPicker"/>.
    /// </summary>
    public class StorageHasherPicker : IStorageHasherPicker
    {
        /// <summary>
        /// <see cref="IStorageHasherPicker.HashProviders"/>.
        /// </summary>
        public ICollection<IHasher> HashProviders { get; }


        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="hashProviders">此选择器使用的哈希提供程序。</param>
        public StorageHasherPicker(IEnumerable<IHasher> hashProviders)
        {
            if (hashProviders == null)
            {
                throw new ArgumentNullException(nameof(hashProviders));
            }

            HashProviders = new Collection<IHasher>(new List<IHasher>(hashProviders));
        }


        /// <summary>
        /// <see cref="IStorageHasherPicker.PickHasher{T}"/>.
        /// </summary>
        public IHasher? PickHasher<T>(string serviceId, string storageProviderInstanceName, string grainType, GrainId grainId, IGrainState<T> grainState, string? tag = null)
        {
            return HashProviders.FirstOrDefault();
        }
    }
}
