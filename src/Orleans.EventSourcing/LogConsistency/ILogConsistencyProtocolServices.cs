using System;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 用于使用自定义一致性或复制协议的日志查看调整器的功能。
    /// 摘要在不同组群中原对数 grain 复制品之间相互交流。
    /// </summary>
    public interface ILogConsistencyProtocolServices
    {
        /// <summary>
        /// 此标识的代号 grain。
        /// </summary>
        GrainId GrainId { get; }

        /// <summary>
        /// 所提供的论据的副本。
        /// </summary>
        T DeepCopy<T>(T value);

        /// <summary>
        /// 此组的代号。 如果没有多组网络, 返回“ I ” 。
        /// </summary>
        /// <returns></returns>
        string MyClusterId { get; }

        /// <summary>
        /// 记录在日志一致性协议中发生的错误。
        /// </summary>
        void ProtocolError(string msg, bool throwexception);

        /// <summary>
        /// 记录在对齐一致性协议中所捕捉的例外。
        /// </summary> 
        void CaughtException(string where, Exception e);

        /// <summary>
        /// 登录用户代码中发生的一些回调的例外
        /// </summary>
        /// <param name="callback">回调的名称</param>
        /// <param name="where">调回调时所根据的背景</param>
        /// <param name="e">被抓获的例外</param>
        void CaughtUserCodeException(string callback, string where, Exception e);

        /// <summary> 在指定的日志级别输出指定的信件。 </summary>
        void Log(LogLevel level, string format, params object[] args);
    }



    /// <summary>
    /// 协议信息层的例外
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
        /// <param name="msg">描述错误的电文。</param>
        public ProtocolTransportException(string msg)
            : base(msg)
        { }

        /// <summary>
        /// 初始化 <see cref="ProtocolTransportException"/> 类的新实例。
        /// </summary>
        /// <param name="msg">描述错误的电文。</param>
        /// <param name="exc">造成运输故障的例外情形。</param>
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
