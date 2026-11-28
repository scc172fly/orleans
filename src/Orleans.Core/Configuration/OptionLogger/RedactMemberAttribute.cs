using Orleans.Runtime.Configuration;
using System;

namespace Orleans
{
    /// <summary>
    /// 将此特性应用于选项类的属性时，符合此约定的 <see cref="IOptionFormatter"/> 实例不会直接格式化该属性的值。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RedactAttribute : Attribute
    {
        /// <summary>
        /// 隐去所提供值中的敏感信息。
        /// </summary>
        /// <param name="value">值。</param>
        /// <returns>隐去敏感信息后的值。</returns>
        public virtual string Redact(object? value)
        {
            return "REDACTED";
        }
    }

    /// <summary>
    /// 将此特性应用于选项类的连接字符串属性时，符合此约定的 <see cref="IOptionFormatter"/> 实例不会直接格式化该属性的值。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RedactConnectionStringAttribute : RedactAttribute
    {
        /// <summary>
        /// 隐去所提供值中的敏感信息。
        /// </summary>
        /// <param name="value">值。</param>
        /// <returns>隐去敏感信息后的值。</returns>
        public override string Redact(object? value)
        {
            return ConfigUtilities.RedactConnectionStringInfo(value as string);
        }
    }
}
