using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace Orleans.Serialization
{
    /// <summary>
    /// 表示其类型在反序列化期间不可用的异常。
    /// </summary>
    [DebuggerDisplay("{" + nameof(GetDebuggerDisplay) + "(),nq}")]
    public sealed class UnavailableExceptionFallbackException : Exception
    {
        /// <inheritdoc />
        public UnavailableExceptionFallbackException()
        {
        }

        /// <inheritdoc />
#if NET8_0_OR_GREATER
        [Obsolete]
#endif
        public UnavailableExceptionFallbackException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            foreach (var pair in info)
            {
                Properties[pair.Name] = pair.Value;
            }
        }

        /// <inheritdoc />
        public UnavailableExceptionFallbackException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 获取异常的已序列化属性。
        /// </summary>
        public Dictionary<string, object?> Properties { get; } = new();

        /// <summary>
        /// 获取异常类型名称。
        /// </summary>
        public string? ExceptionType { get; internal set; }

        /// <inheritdoc />
        public override string ToString() => string.IsNullOrWhiteSpace(ExceptionType) ? $"Unknown exception: {base.ToString()}" : $"Unknown exception of type {ExceptionType}: {base.ToString()}";

        private string GetDebuggerDisplay() => ToString();
    }
}
