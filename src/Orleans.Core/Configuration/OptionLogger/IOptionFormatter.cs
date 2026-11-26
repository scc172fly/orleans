using System.Collections.Generic;

namespace Orleans
{
    /// <summary>
    /// 格式格式化选项,并给它一个类别和一个名称
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
        /// <returns>与选项对象的属性相对应的格式字符串-价值对集合。</returns>
        IEnumerable<string> Format();
    }

    /// <summary>
    /// 特定选项类型的事项的选项 <typeparamref name="T"/>
    /// </summary>
    /// <typeparam name="T">选项类型。</typeparam>
    public interface IOptionFormatter<T> : IOptionFormatter
    {
    }

    /// <summary>
    /// IOptionFormatterResolver 对某些指定选项的具体 OptionFormatter
    /// </summary>
    /// <typeparam name="T">选项类型。</typeparam>
    public interface IOptionFormatterResolver<T>
    {
        /// <summary>
        /// 确定指定选项名称的指定选项类型的事项选项。
        /// </summary>
        /// <param name="name">选项名称。</param>
        /// <returns>选项类型。</returns>
        IOptionFormatter<T> Resolve(string name);
    }

    /// <summary>
    /// 选项格式化的工具类
    /// </summary>
    public static class OptionFormattingUtilities
    {
        /// <summary>
        /// 默认格式字符串。
        /// </summary>
        private const string DefaultFormatFormatting = "{0}: {1}";

        /// <summary>
        /// 指定选项类型默认格式字符串。
        /// </summary>
        private const string DefaultNamedFormatting = "{0}-{1}";

        /// <summary>
        /// 使用默认格式格式格式化键值对
        /// </summary>
        /// <param name="key">
        /// 钥匙
        /// </param>
        /// <param name="value">
        /// 价值。
        /// </param>
        /// <param name="formatting">
        /// 格式字符串。
        /// </param>
        /// <returns>格式化的键值对 。</returns>
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
        /// <returns>格式化选项对象名称。</returns>
        public static string Name<TOptions>(string? name = null, string? formatting = null)
        {
            return name is null && formatting is null ? typeof(TOptions).FullName!
                : string.Format(formatting ?? DefaultNamedFormatting, typeof(TOptions).FullName, name);
        }
    }
}
