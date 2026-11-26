using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 用于报告错误的 Orleans 运行时间所使用的例外类别。
    /// </summary>
    /// <remarks>
    /// 这也是任何更具体例外的基级
    /// 和《公约》所提出 Orleans 运行时间。
    /// </remarks>
    [Serializable]
    [GenerateSerializer]
    public class OrleansException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        public OrleansException()
            : base("Unexpected error.")
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        public OrleansException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        /// <param name="message">
        /// 信息
        /// </param>
        /// <param name="innerException">
        /// 内在的例外
        /// </param>
        public OrleansException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="OrleansException"/> 类的新实例。
        /// </summary>
        /// <param name="info">
        /// 序列信息
        /// </param>
        /// <param name="context">
        /// 背景。
        /// </param>
        /// <exception cref="SerializationException">类名为 <see langword="null" /> 或 <see cref="P:System.Exception.HResult" /> 为零(0)</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 是 <see langword="null" />.</exception>
        [Obsolete]
        protected OrleansException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
