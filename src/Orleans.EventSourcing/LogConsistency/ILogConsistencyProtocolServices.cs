using System;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 供使用自定义一致性或复制协议的日志视图适配器使用的功能。
    /// 对不同集群中的日志一致性 Grain 副本之间的通信进行抽象。
    /// </summary>
    public interface ILogConsistencyProtocolServices
    {
        /// <summary>
        /// 此 Grain 的 ID。
        /// </summary>
        GrainId GrainId { get; }

        /// <summary>
        /// 复制提供的参数。
        /// </summary>
        T DeepCopy<T>(T value);

        /// <summary>
        /// 此集群的 ID。如果不存在多集群网络，则返回“I”。
        /// </summary>
        /// <returns></returns>
        string MyClusterId { get; }

        /// <summary>
        /// 记录日志一致性协议中发生的错误。
        /// </summary>
        void ProtocolError(string msg, bool throwexception);

        /// <summary>
        /// 记录日志一致性协议中捕获的异常。
        /// </summary> 
        void CaughtException(string where, Exception e);

        /// <summary>
        /// 记录某个回调的用户代码中发生的异常。
        /// </summary>
        /// <param name="callback">回调的名称。</param>
        /// <param name="where">调用回调时所处的上下文。</param>
        /// <param name="e">捕获的异常。</param>
        void CaughtUserCodeException(string callback, string where, Exception e);

        /// <summary>以指定的日志级别输出指定消息。</summary>
        void Log(LogLevel level, string format, params object[] args);
    }



    /// <summary>
    /// 协议消息传递层抛出的异常。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class ProtocolTransportException : OrleansException
    {
        /// <summary>
        /// 初始化 <see cref="ProtocolTransportException"/> 类的新实例。
        /// </summary>
        public ProtocolTransportException()
        { }

        /// <summary>
        /// 初始化 <see cref="ProtocolTransportException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">描述错误的消息。</param>
        public ProtocolTransportException(string msg)
            : base(msg)
        { }

        /// <summary>
        /// 初始化 <see cref="ProtocolTransportException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">描述错误的消息。</param>
        /// <param name="exc">导致传输失败的异常。</param>
        public ProtocolTransportException(string msg, Exception exc)
            : base(msg, exc)
        { }

        [Obsolete]
        private ProtocolTransportException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }

        /// <inheritdoc />
        public override string ToString()
        {
            if (InnerException != null)
                return $"ProtocolTransportException: {InnerException}";
            else
                return Message;
        }
    }
}
