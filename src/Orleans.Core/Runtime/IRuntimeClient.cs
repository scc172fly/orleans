using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.CodeGeneration;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
    /// <summary>
    /// IRuntimeClient 接口定义运行时间 API 中同时接触 silo 和客户端的子集。
    /// </summary>
    internal interface IRuntimeClient
    {
        /// <summary>
        /// 获得系统使用的时间提供者。
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// Grain 工厂得到和铸造 grain 参考文献。
        /// </summary>
        IInternalGrainFactory InternalGrainFactory { get; }

        /// <summary>
        /// 当前客户端的独特标识符 。
        /// 此字符串没有语义内容, 但可能对记录有用 。
        /// </summary>
        string CurrentActivationIdentity { get; }

        /// <summary>
        /// 得到了服务供应商。
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 获取当前此客户端的响应超时设置 。
        /// </summary>
        /// <returns>反应超时值</returns>
        TimeSpan GetResponseTimeout();

        /// <summary>
        /// 设置当前此客户端的响应超时设置 。
        /// </summary>
        /// <param name="timeout">新的响应超时值</param>
        void SetResponseTimeout(TimeSpan timeout);

        void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource? context, InvokeMethodOptions options);

        void SendResponse(Message request, Response response);

        void ReceiveResponse(Message message);

        IAddressable CreateObjectReference(IAddressable obj);

        void DeleteObjectReference(IAddressable obj);

        IGrainReferenceRuntime GrainReferenceRuntime { get; }

        void BreakOutstandingMessagesToSilo(SiloAddress deadSilo);

        // 仅用于测试。
        int GetRunningRequestsCount(GrainInterfaceType grainInterfaceType);
    }

    /// <summary>
    /// 用于援引的辅助人员类 <see cref="IOnDeserialized.OnDeserialized"/> 物体上执行 <see cref="IOnDeserialized"/>,在消毒后立即。
    /// </summary>
    public class OnDeserializedCallbacks : DeserializationContext
    {
        /// <summary>
        /// 初始化 <see cref="OnDeserializedCallbacks"/> 类的新实例。
        /// </summary>
        /// <param name="serviceProvider">
        /// 服务供应商。
        /// </param>
        public OnDeserializedCallbacks(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            RuntimeClient = serviceProvider.GetRequiredService<IRuntimeClient>();
        }

        /// <summary>
        /// 得到了服务供应商。
        /// </summary>
        public override IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// 找到运行时间客户
        /// </summary>
        public override object RuntimeClient { get; }

        /// <summary>
        /// 序列化基础设施采用的钩子法。
        /// </summary>
        /// <param name="value">被消毒的值</param>
        public void OnDeserialized(IOnDeserialized value) => value.OnDeserialized(this);
    }
}
