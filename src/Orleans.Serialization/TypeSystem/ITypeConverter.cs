using System;

namespace Orleans.Serialization
{
    /// <summary>
    /// 在 <see cref="Type"/> 和 <see cref="string"/> 两种表示形式之间进行转换。
    /// </summary>
    public interface ITypeConverter
    {
        /// <summary>
        /// 将给定类型格式化为字符串。
        /// </summary>
        bool TryFormat(Type type, out string formatted);

        /// <summary>
        /// 解析给定类型。
        /// </summary>
        bool TryParse(string formatted, out Type type);
    }

    /// <summary>
    /// 用于决定是否允许加载类型，以及是否允许类型参与序列化、反序列化等操作的功能。
    /// </summary>
    public interface ITypeNameFilter
    {
        /// <summary>
        /// 确定指定类型名称是否对应允许加载、序列化、反序列化等操作的类型。
        /// </summary>
        /// <param name="typeName">类型名称。</param>
        /// <param name="assemblyName">程序集名称。</param>
        /// <returns><see langword="true" /> 表示允许指定类型；<see langword="false" /> 表示不允许该类型；<see langword="null" /> 表示此筛选器无法判断该类型。</returns>
        bool? IsTypeNameAllowed(string typeName, string assemblyName);
    }

    /// <summary>
    /// 用于决定是否允许加载类型，以及是否允许类型参与序列化、反序列化等操作的功能。
    /// </summary>
    public interface ITypeFilter
    {
        /// <summary>
        /// 确定是否允许对指定类型进行序列化、反序列化等操作。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <returns><see langword="true" /> 表示允许指定类型；<see langword="false" /> 表示不允许该类型；<see langword="null" /> 表示此筛选器无法判断该类型。</returns>
        bool? IsTypeAllowed(Type type);
    }
}
