using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans
{
    internal static class SetExtensions
    {
        /// <summary>
        /// 从 IEnumerable 创建 HashSet 的便捷方式，支持类型推断
        /// （而标准构造函数不支持类型推断）。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        public static HashSet<T>? ToSet<T>(this IEnumerable<T>? values)
        {
            if (values == null)
                return null;
            return new HashSet<T>(values);
        }

        /// <summary>
        /// 对枚举序列中的每个元素调用 ToString。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="toString">可以传入 null 以使用 Object.ToString()。</param>
        /// <param name="separator">置于每个元素之前；未指定时使用空格。</param>
        /// <returns></returns>
        public static string ToStrings<T>(this IEnumerable<T>? list, Func<T, object?>? toString = null, string separator = " ")
        {
            if (list == null) return "";
            toString = toString ?? (x => x);
            //Func<T, string> toStringPrinter = (x =>
            //    {
            //        object obj = toString(x);
            //        if(obj != null)
            //            return obj.ToString();
            //        else
            //            return "null";
            //    });
            //return Utils.IEnumerableToString(list, toStringPrinter, separator);
            // 不要使用 Aggregate 进行字符串拼接。这样效率很低，会重新分配并复制大量中间字符串。
            //toString = toString ?? (x => x);
            return list.Aggregate("", (s, x) => s + separator + toString(x));
        }

        public static T GetValueOrAddNew<T, TU>(this Dictionary<TU, T> dictionary, TU key) where T : new() where TU : notnull
        {
            T? result;
            if (dictionary.TryGetValue(key, out result))
                return result;

            result = new T();
            dictionary[key] = result;
            return result;
        }
    }
}
