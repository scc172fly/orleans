using System;
using System.Collections.Generic;

namespace Orleans
{
    /// <summary>
    /// 表示开始停用 Grain 的原因。
    /// </summary>
    public readonly struct DeactivationReason : IEquatable<DeactivationReason>
    {
        /// <summary>
        /// 初始化 <see cref="DeactivationReason"/> 结构的新实例。
        /// </summary>
        /// <param name="code">
        /// 标识停用原因的代码。
        /// </param>
        /// <param name="text">
        /// 对停用原因的描述。
        /// </param>
        public DeactivationReason(DeactivationReasonCode code, string text)
        {
            ReasonCode = code;
            Description = text;
            Exception = null;
        }

        /// <summary>
        /// 初始化 <see cref="DeactivationReason"/> 结构的新实例。
        /// </summary>
        /// <param name="code">
        /// 标识停用原因的代码。
        /// </param>
        /// <param name="exception">
        /// 导致停用的异常。
        /// </param>
        /// <param name="text">
        /// 对停用原因的描述。
        /// </param>
        public DeactivationReason(DeactivationReasonCode code, Exception? exception, string text)
        {
            ReasonCode = code;
            Description = text;
            Exception = exception;
        }

        /// <summary>
        /// 获取停用原因的描述。
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 获取停用原因。
        /// </summary>
        public DeactivationReasonCode ReasonCode { get; }

        /// <summary>
        /// 获取导致停用的异常。
        /// </summary>
        public Exception? Exception { get; }

        /// <inheritdoc/>
        public bool Equals(DeactivationReason other) =>
            ReasonCode == other.ReasonCode
            && Description == other.Description
            && EqualityComparer<Exception?>.Default.Equals(Exception, other.Exception);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is DeactivationReason other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(ReasonCode, Description, Exception);

        /// <summary>
        /// 判断两个停用原因是否相等。
        /// </summary>
        public static bool operator ==(DeactivationReason left, DeactivationReason right) => left.Equals(right);

        /// <summary>
        /// 判断两个停用原因是否不相等。
        /// </summary>
        public static bool operator !=(DeactivationReason left, DeactivationReason right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Exception is not null)
            {
                return $"{ReasonCode}: {Description}. Exception: {Exception}";
            }

            return $"{ReasonCode}: {Description}";
        }
    }
}
