using Orleans.Runtime.Configuration;
using System;

namespace Orleans
{
    /// <summary>
    /// 当应用到选项类别上的属性时, 此属性将防止通过符合 <see cref="IOptionFormatter"/> 实例的格式格式化属性值。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RedactAttribute : Attribute
    {
        /// <summary>
        /// 重编所提供的值 。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>修改的值。</returns>
        public virtual string Redact(object? value)
        {
            return "REDACTED";
        }
    }

    /// <summary>
    /// 当应用到选项类上的连接字符串属性时, 此属性将防止属性值通过符合 <see cref="IOptionFormatter"/> 实例的格式格式化。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RedactConnectionStringAttribute : RedactAttribute
    {
        /// <summary>
        /// 重编所提供的值 。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>修改的值。</returns>
        public override string Redact(object? value)
        {
            return ConfigUtilities.RedactConnectionStringInfo(value as string);
        }
    }
}
