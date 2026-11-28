using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 指示 <see cref="GrainReference"/> 在使用前尚未绑定到运行时。
    /// </summary>
    [Serializable, GenerateSerializer]
    public sealed class GrainReferenceNotBoundException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="grainReference">未绑定的 Grain 引用。</param>
        internal GrainReferenceNotBoundException(GrainReference grainReference) : base(CreateMessage(grainReference)) { }

        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        internal GrainReferenceNotBoundException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        internal GrainReferenceNotBoundException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="GrainReferenceNotBoundException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
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
