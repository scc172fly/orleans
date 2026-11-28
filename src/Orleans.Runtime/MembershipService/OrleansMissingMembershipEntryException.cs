using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 表示预期存在的集群成员资格条目的异常。
    /// </summary>
    /// <seealso cref="Orleans.Runtime.OrleansException" />
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansMissingMembershipEntryException : OrleansException
    {

        /// <summary>
        /// 初始化 <see cref="OrleansMissingMembershipEntryException"/> 类的新实例。
        /// </summary>
        public OrleansMissingMembershipEntryException() : base("Membership table does not contain information an entry for this silo.") { }

        /// <summary>
        /// 初始化 <see cref="OrleansMissingMembershipEntryException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public OrleansMissingMembershipEntryException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="OrleansMissingMembershipEntryException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public OrleansMissingMembershipEntryException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="OrleansMissingMembershipEntryException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private OrleansMissingMembershipEntryException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
