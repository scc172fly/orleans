using System.Collections.Generic;

namespace Orleans
{
    /// <summary>
    /// 格式化选项，并为其指定类别和名称。
    /// </summary>
    public interface IOptionFormatter
    {
        /// <summary>
        /// 获取选项对象的名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 将选项对象格式化为字符串集合。
        /// </summary>
        /// <returns>与选项对象的属性对应的、已格式化的字符串键值对集合。</returns>
        IEnumerable<string> Format();
    }

    /// <summary>
    /// 特定选项类型 <typeparamref name="T"/> 的选项格式化器。
    /// </summary>
    /// <typeparam name="T">选项类型。</typeparam>
    public interface IOptionFormatter<T> : IOptionFormatter
    {
    }

    /// <summary>
    /// IOptionFormatterResolver 用于解析特定命名选项对应的 OptionFormatter。
    /// </summary>
    /// <typeparam name="T">选项类型。</typeparam>
    public interface IOptionFormatterResolver<T>
    {
        /// <summary>
        /// 解析具有指定选项类型和选项名称的选项格式化器。
        /// </summary>
        /// <param name="name">选项名称。</param>
        /// <returns>选项类型。</returns>
        IOptionFormatter<T> Resolve(string name);
    }

    /// <summary>
    /// 用于格式化选项的实用工具类。
    /// </summary>
    public static class OptionFormattingUtilities
    {
        /// <summary>
        /// 默认格式字符串。
        /// </summary>
        private const string DefaultFormatFormatting = "{0}: {1}";

        /// <summary>
        /// 命名选项类型的默认格式字符串。
        /// </summary>
        private const string DefaultNamedFormatting = "{0}-{1}";

        /// <summary>
        /// 使用默认格式对键值对进行格式化。
        /// </summary>
        /// <param name="key">
        /// 键。
        /// </param>
        /// <param name="value">
        /// 值。
        /// </param>
        /// <param name="formatting">
        /// 格式字符串。
        /// </param>
        /// <returns>格式化后的键值对。</returns>
        public static string Format(object? key, object? value, string? formatting = null)
        {
            var valueFormat = formatting ?? DefaultFormatFormatting;
            return string.Format(valueFormat, key, value);
        }

        /// <summary>
        /// 格式化选项对象的名称。
        /// </summary>
        /// <typeparam name="TOptions">选项类型。</typeparam>
        /// <param name="name">选项名称。</param>
        /// <param name="formatting">格式字符串。</param>
        /// <returns>格式化后的选项对象名称。</returns>
        public static string Name<TOptions>(string? name = null, string? formatting = null)
        {
            return name is null && formatting is null ? typeof(TOptions).FullName!
                : string.Format(formatting ?? DefaultNamedFormatting, typeof(TOptions).FullName, name);
        }
    }
}
