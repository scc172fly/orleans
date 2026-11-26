using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 音箱 grain 目录条目。
    /// </summary>
    public interface IGrainDirectoryCache
    {
        /// <summary>
        /// 将带有给定版本的新条目添加到缓存中:密钥 (grain) -- > 值
        /// 新条目将超过给定密钥下的任何现有条目, 
        /// 不论储存版本
        /// </summary>
        /// <param name="value">值以添加</param>
        /// <param name="version">值版本的版本</param>
        void AddOrUpdate(GrainAddress value, int version);

        /// <summary>
        /// 以密钥为条件, 从缓存中删除条目
        /// </summary>
        /// <param name="key">要删除的密钥</param>
        /// <returns>如果条目在缓存中且清除成功, 则正确</returns>
        bool Remove(GrainId key);

        /// <summary>
        /// 以密钥为条件, 从缓存中删除条目
        /// </summary>
        /// <param name="key">要删除的密钥</param>
        /// <returns>如果条目在缓存中且清除成功, 则正确</returns>
        bool Remove(GrainAddress key);

        /// <summary>
        /// 清除缓存, 删除所有条目 。
        /// </summary>
        void Clear();

        /// <summary>
        /// 根据给定密钥查找缓存值和版本
        /// </summary>
        /// <param name="key">查找的密钥</param>
        /// <param name="result">如果找到密钥,则未另定义的键值</param>
        /// 如果找到密钥, 则未另定义的缓存值<param name="version">版本</param>
        /// <returns>如果给定的密钥在缓存</returns>中, 则是真
        bool LookUp(GrainId key, [NotNullWhen(true)] out GrainAddress? result, out int version);

        /// <summary>
        /// 返回当前存储在缓存中的键值转换图盘列表 。
        /// </summary>
        IEnumerable<(GrainAddress ActivationAddress, int Version)> KeyValues { get; }
    }

    internal static class GrainDirectoryCacheExtensions
    {
        /// <summary>
        /// 查找给定密钥的缓存值 。
        /// </summary>
        /// <param name="cache">grain 目录缓存,以查找</param>的结果
        /// <param name="key">查找的密钥</param>
        /// <param name="result">如果找到密钥,则未另定义的键值</param>
        /// <returns>如果给定的密钥在缓存</returns>中, 则是真
        public static bool LookUp(this IGrainDirectoryCache cache, GrainId key, [NotNullWhen(true)] out GrainAddress? result)
        {
            return cache.LookUp(key, out result, out _);
        }
    }
}
