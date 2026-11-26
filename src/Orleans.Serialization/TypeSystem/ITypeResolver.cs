using System;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 提供从字符串中解析 <see cref="Type"/> 的方法。
    /// </summary>
    public abstract class TypeResolver
    {
        /// <summary>
        /// 返回 <see cref="Type"/> 所提供相应数额的 <paramref name="name"/>,如果决议失败,则予以例外。
        /// </summary>
        /// <param name="name">类型名称。</param>
        /// <returns>下 <see cref="Type"/> 所提供相应数额的 <paramref name="name"/>.</returns>
        public abstract Type ResolveType(string name);

        /// <summary>
        /// 决定 <see cref="Type"/> 所提供相应数额的 <paramref name="name" />如果决议成功,否则将失败而复返原状。
        /// </summary>
        /// <param name="name">类型名称。</param>
        /// <param name="type">解决型号。</param>
        /// <returns><see langword="true"/> 解决办法如获成功; <see langword="false"/> 否则。</returns>
        public abstract bool TryResolveType(string name, out Type type);
    }
}
