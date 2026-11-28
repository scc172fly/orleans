using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// IRuntimeClient 接口定义同时向 Silo 和客户端公开的运行时 API 子集。
    /// </summary>
    internal interface IRuntimeClient
    {
        /// <summary>
        /// 获取系统使用的时间提供程序。
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// 用于获取及转换 Grain 引用的 Grain 工厂。
        /// </summary>
        IInternalGrainFactory InternalGrainFactory { get; }

        /// <summary>
        /// 当前客户端的唯一标识符。
        /// 此字符串没有语义含义，但可用于日志记录。
        /// </summary>
        string CurrentActivationIdentity { get; }

        /// <summary>
        /// 获取服务提供程序。
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取此客户端当前的响应超时设置。
        /// </summary>
        /// <returns>响应超时值。</returns>
        TimeSpan GetResponseTimeout();

        /// <summary>
        /// 设置此客户端当前的响应超时设置。
        /// </summary>
        /// <param name="timeout">新的响应超时值。</param>
        void SetResponseTimeout(TimeSpan timeout);

        void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource? context, InvokeMethodOptions options);

        void SendResponse(Message request, Response response);

        void ReceiveResponse(Message message);

        IAddressable CreateObjectReference(IAddressable obj);

        void DeleteObjectReference(IAddressable obj);

        IGrainReferenceRuntime GrainReferenceRuntime { get; }

        void BreakOutstandingMessagesToSilo(SiloAddress deadSilo);

        // 仅供测试使用。
        int GetRunningRequestsCount(GrainInterfaceType grainInterfaceType);
    }

    /// <summary>
    /// 辅助类，用于在反序列化结束后立即调用 <see cref="IOnDeserialized.OnDeserialized"/>，调用对象为实现 <see cref="IOnDeserialized"/> 的对象。
    /// </summary>
    public class OnDeserializedCallbacks : DeserializationContext
    {
        /// <summary>
        /// 初始化 <see cref="OnDeserializedCallbacks"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务提供程序。
        /// </param>
        public OnDeserializedCallbacks(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            RuntimeClient = serviceProvider.GetRequiredService<IRuntimeClient>();
        }

        /// <summary>
        /// 获取服务提供程序。
        /// </summary>
        public override IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取运行时客户端。
        /// </summary>
        public override object RuntimeClient { get; }

        /// <summary>
        /// 序列化基础设施调用的钩子方法。
        /// </summary>
        /// <param name="value">已反序列化的值。</param>
        public void OnDeserialized(IOnDeserialized value) => value.OnDeserialized(this);
    }
}
