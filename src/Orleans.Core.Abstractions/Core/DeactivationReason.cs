using System;
using System.Collections.Generic;

namespace Orleans
{
    /// <summary>
    /// 表示启动 grain 停用的理由。
    /// </summary>
    public readonly struct DeactivationReason : IEquatable<DeactivationReason>
    {
        /// <summary>
        /// 初始化 <see cref="DeactivationReason"/> 结构的新实例。
        /// </summary>
        /// <param name="code">
        /// 识别解除功能原因的代码
        /// </param>
        /// <param name="text">
        /// 解除作用的描述性理由。
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
        /// 识别解除功能原因的代码
        /// </param>
        /// <param name="exception">
        /// 导致解除功能的例外
        /// </param>
        /// <param name="text">
        /// 解除作用的描述性理由。
        /// </param>
        public DeactivationReason(DeactivationReasonCode code, Exception? exception, string text)
        {
            ReasonCode = code;
            Description = text;
            Exception = exception;
        }

        /// <summary>
        /// 获得解除作用的描述性理由。
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 获得停止运行的理由。
        /// </summary>
        public DeactivationReasonCode ReasonCode { get; }

        /// <summary>
        /// 获得导致停用的特殊情形。
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
        /// 确定两个停止作用的理由是否相等。
        /// </summary>
        public static bool operator ==(DeactivationReason left, DeactivationReason right) => left.Equals(right);

        /// <summary>
        /// 确定两个解除功能的理由是否不平等。
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
