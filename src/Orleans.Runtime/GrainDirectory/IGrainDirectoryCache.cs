using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 缓存 Grain 目录条目。
    /// </summary>
    public interface IGrainDirectoryCache
    {
        /// <summary>
        /// 按给定版本向缓存添加新条目：键（Grain）映射到值。
        /// 新条目会覆盖给定键下任何已有条目，
        /// 而不考虑已存储的版本。
        /// </summary>
        /// <param name="value">要添加的值。</param>
        /// <param name="version">该值的版本。</param>
        void AddOrUpdate(GrainAddress value, int version);

        /// <summary>
        /// 根据给定键从缓存移除条目。
        /// </summary>
        /// <param name="key">要移除的条目的键。</param>
        /// <returns>如果条目存在于缓存且已成功移除，则为 true。</returns>
        bool Remove(GrainId key);

        /// <summary>
        /// 根据给定键从缓存移除条目。
        /// </summary>
        /// <param name="key">要移除的条目的键。</param>
        /// <returns>如果条目存在于缓存且已成功移除，则为 true。</returns>
        bool Remove(GrainAddress key);

        /// <summary>
        /// 清空缓存，删除所有条目。
        /// </summary>
        void Clear();

        /// <summary>
        /// 根据给定键查找缓存值及其版本。
        /// </summary>
        /// <param name="key">用于查找的键。</param>
        /// <param name="result">若找到键，则为对应的值，否则未定义。</param>
        /// <param name="version">若找到键，则为缓存值的版本，否则未定义。</param>
        /// <returns>如果给定键存在于缓存，则为 true。</returns>
        bool LookUp(GrainId key, [NotNullWhen(true)] out GrainAddress? result, out int version);

        /// <summary>
        /// 返回当前缓存中存储的键、值和版本三元组列表。
        /// </summary>
        IEnumerable<(GrainAddress ActivationAddress, int Version)> KeyValues { get; }
    }

    internal static class GrainDirectoryCacheExtensions
    {
        /// <summary>
        /// 根据给定键查找缓存值。
        /// </summary>
        /// <param name="cache">从中查找结果的 Grain 目录缓存。</param>
        /// <param name="key">用于查找的键。</param>
        /// <param name="result">若找到键，则为对应的值，否则未定义。</param>
        /// <returns>如果给定键存在于缓存，则为 true。</returns>
        public static bool LookUp(this IGrainDirectoryCache cache, GrainId key, [NotNullWhen(true)] out GrainAddress? result)
        {
            return cache.LookUp(key, out result, out _);
        }
    }
}
