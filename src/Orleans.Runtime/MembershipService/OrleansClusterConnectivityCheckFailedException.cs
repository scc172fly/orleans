using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 例外用于表示集束连接检查失败。
    /// </summary>
    /// <seealso cref="Orleans.Runtime.OrleansException" />
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansClusterConnectivityCheckFailedException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="OrleansClusterConnectivityCheckFailedException"/> 类的新实例。
        /// </summary>
        public OrleansClusterConnectivityCheckFailedException() : base("Failed to verify connectivity with active cluster nodes.") { }

        /// <summary>
        /// 初始化 <see cref="OrleansClusterConnectivityCheckFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        public OrleansClusterConnectivityCheckFailedException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="OrleansClusterConnectivityCheckFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public OrleansClusterConnectivityCheckFailedException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="OrleansClusterConnectivityCheckFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private OrleansClusterConnectivityCheckFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
