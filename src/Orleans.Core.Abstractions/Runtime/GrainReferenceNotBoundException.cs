using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示 <see cref="GrainReference"/> 在使用前未受运行时间约束。
    /// </summary>
    [Serializable, GenerateSerializer]
    public sealed class GrainReferenceNotBoundException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="grainReference">无约束的 grain 参考。</param>
        internal GrainReferenceNotBoundException(GrainReference grainReference) : base(CreateMessage(grainReference)) { }

        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        internal GrainReferenceNotBoundException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        internal GrainReferenceNotBoundException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private GrainReferenceNotBoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        private static string CreateMessage(GrainReference grainReference)
        {
            return $"Attempted to use an invalid GrainReference, which has not been constructed by the runtime: {grainReference}.";
        }
    }
}
