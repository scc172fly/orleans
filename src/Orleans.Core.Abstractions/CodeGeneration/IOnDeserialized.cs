using System;

namespace Orleans.Serialization
{
    /// <summary>
    /// 表示在某一类已消毒后,应通知该类。
    /// </summary>
    public interface IOnDeserialized
    {
        /// <summary>
        /// 通知这一事例,它已完全消毒。
        /// </summary>
        /// <param name="context">序列器上下文。</param>
        void OnDeserialized(DeserializationContext context);
    }

    /// <summary>
    /// 提供与一次取消飞行作业有关的服务和运行时间。
    /// </summary>
    public abstract class DeserializationContext
    {
        /// <summary>
        /// 获得与消毒操作有关的服务供应商。
        /// </summary>
        public abstract IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取与拆解操作相关的运行时客户端 Orleans 。
        /// </summary>
        public abstract object RuntimeClient { get; }
    }
}
