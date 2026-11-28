using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime
{
    /// <summary>
    /// 表示配置异常。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansConfigurationException : Exception
    {
        /// <inheritdoc />
        public OrleansConfigurationException(string message)
            : base(message)
        {
        }

        /// <inheritdoc />
        public OrleansConfigurationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <inheritdoc />
        /// <exception cref="SerializationException">类名为 <see langword="null" />，或者 <see cref="P:System.Exception.HResult" /> 为零（0）。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> 为 <see langword="null" />。</exception>
        [Obsolete]
        private OrleansConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
