using System;
using System.Collections.Generic;
using System.Linq;

namespace Orleans
{
    internal static class SetExtensions
    {
        /// <summary>
        /// 从 IE 创建 HashSet 的快捷键
        /// (标准建筑师没有)
        /// </summary>
        /// <typeparam name="T">元素类型</typeparam>
        public static HashSet<T>? ToSet<T>(this IEnumerable<T>? values)
        {
            if (values == null)
                return null;
            return new HashSet<T>(values);
        }

        /// <summary>
        /// ToString 查点的每个要素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="toString">无法供应无效,无法使用 Object.ToString()</param>
        /// <param name="separator">在每个元素之前,或空格(如果未指定)</param>
        /// <returns></returns>
        public static string ToStrings<T>(this IEnumerable<T>? list, Func<T, object?>? toString = null, string separator = " ")
        {
            if (list == null) return "";
            toString = toString ?? (x => x);
            //菲科<T, string> toStringPrinter = (x => 
            //    {
            //        obj 对象 = toString(x);
            //        if(obj != null)
            //            return obj.ToString();
            //        其它
            //            return "null";
            //    });
            //return Utils.IEnumerableToString(list, toStringPrinter, separator);
            //不要使用总和来连接字符串。 它效率非常低, 将会重新分配并复制中间字符串的众多内容 。
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
