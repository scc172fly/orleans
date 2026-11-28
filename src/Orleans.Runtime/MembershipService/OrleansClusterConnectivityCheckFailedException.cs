using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 表示集群连通性检查失败的异常。
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
        /// <param name="message">消息。</param>
        public OrleansClusterConnectivityCheckFailedException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="OrleansClusterConnectivityCheckFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public OrleansClusterConnectivityCheckFailedException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="OrleansClusterConnectivityCheckFailedException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private OrleansClusterConnectivityCheckFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
