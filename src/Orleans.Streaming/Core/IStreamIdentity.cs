using System;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 唯一标识一个流。
    /// </summary>
    /// <remarks>
    /// 尽可能改用 <see cref="StreamId"/>。
    /// </remarks>
    public interface IStreamIdentity
    {
        /// <summary>
        /// 获取唯一标识符。
        /// </summary>
        /// <value>唯一标识符。</value>
        Guid Guid { get; }

        /// <summary>
        /// 获取命名空间。
        /// </summary>
        /// <value>命名空间。</value>
        string? Namespace { get; }
    }
}
