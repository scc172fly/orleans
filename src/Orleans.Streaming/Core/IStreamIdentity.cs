using System;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 独特地识别了一条流。
    /// </summary>
    /// <remarks>
    /// 在可能的情况下,使用 <see cref="StreamId"/> 代替。
    /// </remarks>
    public interface IStreamIdentity
    {
        /// <summary>
        /// 获得唯一识别符。
        /// </summary>
        /// <value>唯一标识符。</value>
        Guid Guid { get; }

        /// <summary>
        /// 获得命名空间。
        /// </summary>
        /// <value>命名空间。</value>
        string? Namespace { get; }
    }
}
