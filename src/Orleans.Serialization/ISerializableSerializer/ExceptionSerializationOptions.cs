using System;
using System.Collections.Generic;

namespace Orleans.Serialization
{
    /// <summary>
    /// 异常序列化选项。
    /// </summary>
    public class ExceptionSerializationOptions
    {
        /// <summary>
        /// 获取异常序列化器支持的命名空间前缀集合。
        /// 命名空间具有其中任一前缀的异常类型都将使用该异常序列化器进行序列化。
        /// </summary>
        public HashSet<string> SupportedNamespacePrefixes { get; } = new HashSet<string>(StringComparer.Ordinal) { "Microsoft", "System", "Azure" };

        /// <summary>
        /// 获取或设置用于启用异常类型序列化的谓词。
        /// </summary>
        public Func<Type, bool> SupportedExceptionTypeFilter { get; set; } = _ => false;
    }
}
