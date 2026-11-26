using System;
using System.Runtime.Serialization;
using System.Security;
using Orleans.Serialization.Codecs;

namespace Orleans.Serialization
{
    /// <summary>
    /// 当一种类型没有序列构建器时扔。
    /// </summary>
    [Serializable]
    public class SerializationConstructorNotFoundException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="SerializationConstructorNotFoundException"/> 类的新实例。
        /// </summary>
        /// <param name="type">类型。</param>
        [SecurityCritical]
        public SerializationConstructorNotFoundException(Type type) : base(GetMessage(type))
        {
        }

        /// <summary>
        /// 初始化 <see cref="SerializationConstructorNotFoundException" /> 类的新实例。
        /// </summary>
        /// <param name="info">序列资料</param>
        /// <param name="context">上下文。</param>
        [SecurityCritical]
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        protected SerializationConstructorNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        private static string GetMessage(Type type)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(type);
            return $"Could not find a suitable serialization constructor on type {type.FullName}";
        }
    }
}
