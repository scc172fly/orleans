using System;
using System.Runtime.Serialization;

namespace Orleans.Runtime.MembershipService
{
    /// <summary>
    /// 例外之处用来表明,预计会出现分组成员进入的情况。
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
        /// <param name="message">电文。</param>
        public OrleansMissingMembershipEntryException(string message) : base(message) { }

        /// <summary>
        /// 初始化 <see cref="OrleansMissingMembershipEntryException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public OrleansMissingMembershipEntryException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// 初始化 <see cref="OrleansMissingMembershipEntryException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private OrleansMissingMembershipEntryException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
