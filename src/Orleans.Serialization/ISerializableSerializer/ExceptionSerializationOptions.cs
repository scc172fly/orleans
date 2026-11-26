using System;
using System.Collections.Generic;

namespace Orleans.Serialization
{
    /// <summary>
    /// 例外序列化的备选办法。
    /// </summary>
    public class ExceptionSerializationOptions
    {
        /// <summary>
        /// 获取例外序列化器的辅助命名空间前缀收藏。
        /// 任何带有这些前缀之一的命名空间的例外类型,都将使用例外序列器进行序列化。
        /// </summary>
        public HashSet<string> SupportedNamespacePrefixes { get; } = new HashSet<string>(StringComparer.Ordinal) { "Microsoft", "System", "Azure" };

        /// <summary>
        /// 获取或设置用于为例外类型进行序列化的前提。
        /// </summary>
        public Func<Type, bool> SupportedExceptionTypeFilter { get; set; } = _ => false;
    }
}
