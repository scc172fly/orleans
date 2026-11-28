using System;

namespace Orleans.Serialization.TypeSystem
{
    /// <summary>
    /// 提供从字符串解析 <see cref="Type"/> 的方法。
    /// </summary>
    public abstract class TypeResolver
    {
        /// <summary>
        /// 返回 <see cref="Type"/>（与给定的 <paramref name="name"/> 对应），如果解析失败则抛出异常。
        /// </summary>
        /// <param name="name">类型名称。</param>
        /// <returns><see cref="Type"/>，与给定的 <paramref name="name"/> 对应。</returns>
        public abstract Type ResolveType(string name);

        /// <summary>
        /// 解析 <see cref="Type"/>（与给定的 <paramref name="name" /> 对应），如果解析成功则返回 true，否则返回 false。
        /// </summary>
        /// <param name="name">类型名称。</param>
        /// <param name="type">解析得到的类型。</param>
        /// <returns><see langword="true"/>，如果解析成功；否则为 <see langword="false"/>。</returns>
        public abstract bool TryResolveType(string name, out Type type);
    }
}
