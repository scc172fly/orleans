using System;

namespace Orleans.Serialization
{
    /// <summary>
    /// 在 <see cref="Type"/> 和 <see cref="string"/> 表示式之间转换。
    /// </summary>
    public interface ITypeConverter
    {
        /// <summary>
        /// 格式化所提供类型为字符串。
        /// </summary>
        bool TryFormat(Type type, out string formatted);

        /// <summary>
        /// 描述提供的类型。
        /// </summary>
        bool TryParse(string formatted, out Type type);
    }

    /// <summary>
    /// 允许装载类型并参与序列化、解除音频化等功能。
    /// </summary>
    public interface ITypeNameFilter
    {
        /// <summary>
        /// 确定指定类型名称是否与允许装入、序列、取消飞行等的类型相符。
        /// </summary>
        /// <param name="typeName">类型的名称。</param>
        /// <param name="assemblyName">组装名称。</param>
        /// <returns><see langword="true" /> 如果允许特定类型; <see langword="false" /> 如果不允许使用该类型; <see langword="null" /> 如果此过滤器不知道该类型, 则该类型为未知。</returns>
        bool? IsTypeNameAllowed(string typeName, string assemblyName);
    }

    /// <summary>
    /// 允许装载类型并参与序列化、解除音频化等功能。
    /// </summary>
    public interface ITypeFilter
    {
        /// <summary>
        /// 确定是否允许对指定类型进行序列化、去序列化等。
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns><see langword="true" /> 如果允许特定类型; <see langword="false" /> 如果不允许使用该类型; <see langword="null" /> 如果此过滤器不知道该类型, 则该类型为未知。</returns>
        bool? IsTypeAllowed(Type type);
    }
}
