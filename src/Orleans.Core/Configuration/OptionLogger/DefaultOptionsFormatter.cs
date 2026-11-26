using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace Orleans
{
    /// <summary>
    /// <see cref="IOptionFormatter{T}"/>的默认执行。
    /// </summary>
    /// <typeparam name="T">选项类型。</typeparam>
    internal sealed class DefaultOptionsFormatter<T> : IOptionFormatter<T> where T : class
    {
        private readonly T _options;

        /// <summary>
        /// 初始化 <see cref="DefaultOptionsFormatter{T}"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项。</param>
        public DefaultOptionsFormatter(IOptions<T> options)
        {
            _options = options.Value;
            Name = OptionFormattingUtilities.Name<T>();
        }

        /// <summary>
        /// 初始化 <see cref="DefaultOptionsFormatter{T}"/> 类的新实例。
        /// </summary>
        /// <param name="name">选项名称。</param>
        /// <param name="options">选项。</param>
        internal DefaultOptionsFormatter(string name, T options)
        {
            _options = options;
            Name = OptionFormattingUtilities.Name<T>(name);
        }

        /// <summary>
        /// 获取选项名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 用于
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> Format()
        {
            foreach (var prop in typeof(T).GetProperties())
            {
                if (!IsFormattableProperty(prop))
                {
                    continue;
                }

                foreach (var formattedValue in FormatProperty(_options, prop))
                {
                    yield return formattedValue;
                }
            }
        }

        private static bool IsFormattableProperty(PropertyInfo prop)
        {
            if (prop is null) return false;
            if (!IsFormattableType(prop.PropertyType)) return false;
            if (prop.GetCustomAttribute<ObsoleteAttribute>() is not null) return false;
            if (!IsAccessibleMethod(prop.GetSetMethod())) return false;
            if (!IsAccessibleMethod(prop.GetGetMethod())) return false;
            return true;
        }

        private static bool IsAccessibleMethod(MethodInfo? accessor)
        {
            if (accessor is null) return false;
            if (!accessor.IsPublic) return false;
            if (accessor.GetCustomAttribute<ObsoleteAttribute>() is not null) return false;
            return true;
        }

        private static bool IsFormattableType(Type type)
        {
            if (type is null) return false;
            if (typeof(Delegate).IsAssignableFrom(type)) return false;
            return true;
        }

        private static IEnumerable<string> FormatProperty(object options, PropertyInfo property)
        {
            var name = property.Name;
            var value = property.GetValue(options);
            var redactAttribute = property.GetCustomAttribute<RedactAttribute>(inherit: true);

            // 如果指定了编辑, 则让属性执行来完成工作
            if (redactAttribute != null)
            {
                yield return OptionFormattingUtilities.Format(name, redactAttribute.Redact(value));
            }
            else
            {
                if (value is IDictionary dict)
                {
                    // 如果是字典 - > 每个项目一行
                    var enumerator = dict.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var kvp = enumerator.Entry;
                        yield return $"{name}.{kvp.Key}: {kvp.Value}";
                    }
                }
                else if (value is ICollection coll)
                {
                    // 如果是简单收藏 - > 每个项目一行
                    if (coll.Count > 0)
                    {
                        var index = 0;
                        foreach (var item in coll)
                        {
                            yield return $"{name}.{index}: {item}";
                            index++;
                        }
                    }
                }
                else
                {
                    // 简单案件
                    yield return OptionFormattingUtilities.Format(name, value);
                }
            }
        }
    }

    internal class DefaultOptionsFormatterResolver<T> : IOptionFormatterResolver<T> where T : class
    {
        private readonly IOptionsMonitor<T> _optionsMonitor;

        public DefaultOptionsFormatterResolver(IOptionsMonitor<T> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        public IOptionFormatter<T> Resolve(string name) => new DefaultOptionsFormatter<T>(name, _optionsMonitor.Get(name));
    }
}
