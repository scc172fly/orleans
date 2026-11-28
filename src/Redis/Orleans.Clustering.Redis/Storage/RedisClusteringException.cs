using System;
using System.Runtime.Serialization;

namespace Orleans.Clustering.Redis
{
    /// <summary>
    /// 表示 Redis 集群成员管理中发生的异常。
    /// </summary>
    [Serializable]
    public class RedisClusteringException : Exception
    {
        /// <inheritdoc/>
        public RedisClusteringException() : base() { }

        /// <inheritdoc/>
        public RedisClusteringException(string message) : base(message) { }

        /// <inheritdoc/>
        public RedisClusteringException(string message, Exception innerException) : base(message, innerException) { }

        /// <inheritdoc/>
        [Obsolete]
        protected RedisClusteringException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
