using System;

namespace Orleans.Serialization
{
    /// <summary>
    /// 指示类应在完成反序列化时收到通知。
    /// </summary>
    public interface IOnDeserialized
    {
        /// <summary>
        /// 通知此实例已完成反序列化。
        /// </summary>
        /// <param name="context">序列化器上下文。</param>
        void OnDeserialized(DeserializationContext context);
    }

    /// <summary>
    /// 提供与反序列化操作相关的服务和运行时状态。
    /// </summary>
    public abstract class DeserializationContext
    {
        /// <summary>
        /// 获取与反序列化操作关联的服务提供程序。
        /// </summary>
        public abstract IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取与反序列化操作关联的 Orleans 运行时客户端。
        /// </summary>
        public abstract object RuntimeClient { get; }
    }
}
