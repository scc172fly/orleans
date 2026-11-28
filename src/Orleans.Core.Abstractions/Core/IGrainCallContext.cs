using System.Reflection;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Orleans
{
    /// <summary>
    /// 用于拦截请求调用的委托。
    /// </summary>
    /// <param name="context">调用上下文。</param>
    /// <returns>必须等待此 <see cref="Task"/> 完成后，才能继续处理。</returns>
    public delegate Task GrainCallFilterDelegate(IGrainCallContext context);

    /// <summary>
    /// 用于拦截传入请求的委托。
    /// </summary>
    /// <param name="context">调用上下文。</param>
    /// <returns>必须等待此 <see cref="Task"/> 完成后，才能继续处理。</returns>
    public delegate Task OutgoingGrainCallFilterDelegate(IOutgoingGrainCallContext context);

    /// <summary>
    /// 用于拦截传出请求的委托。
    /// </summary>
    /// <param name="context">调用上下文。</param>
    /// <returns>必须等待此 <see cref="Task"/> 完成后，才能继续处理。</returns>
    public delegate Task IncomingGrainCallFilterDelegate(IIncomingGrainCallContext context);

    /// <summary>
    /// 表示一次方法调用及其调用结果。
    /// </summary>
    public interface IGrainCallContext
    {
        /// <summary>
        /// 获取请求。
        /// </summary>
        IInvokable Request { get; }

        /// <summary>
        /// 获取被调用的 Grain。
        /// </summary>
        object Grain { get; }

        /// <summary>
        /// 获取调用源的标识（如果可用）。
        /// </summary>
        GrainId? SourceId { get; }

        /// <summary>
        /// 获取目标的标识。
        /// </summary>
        GrainId TargetId { get; }

        /// <summary>
        /// 获取被调用接口的类型。
        /// </summary>
        GrainInterfaceType InterfaceType { get; }

        /// <summary>
        /// 获取被调用接口的名称。
        /// </summary>
        string InterfaceName { get; }

        /// <summary>
        /// 获取被调用方法的名称。
        /// </summary>
        string MethodName { get; }

        /// <summary>
        /// 获取被调用接口方法的 <see cref="MethodInfo"/>。
        /// </summary>
        MethodInfo InterfaceMethod { get; }

        /// <summary>
        /// 获取或设置结果。
        /// </summary>
        object? Result { get; set; }

        /// <summary>
        /// 获取或设置响应。
        /// </summary>
        Response? Response { get; set; }

        /// <summary>
        /// 调用请求。
        /// </summary>
        /// <returns>
        /// 表示此次调用的 <see cref="Task"/>。
        /// </returns>
        Task Invoke();
    }

    /// <summary>
    /// 表示一次传入的方法调用及其调用结果。
    /// </summary>
    public interface IIncomingGrainCallContext : IGrainCallContext
    {
        /// <summary>
        /// 获取目标 Grain 的上下文。
        /// </summary>
        public IGrainContext TargetContext { get; }

        /// <summary>
        /// 获取被调用的实现方法的 <see cref="MethodInfo"/>。
        /// </summary>
        MethodInfo ImplementationMethod { get; }
    }

    /// <summary>
    /// 表示一次传出的方法调用及其调用结果。
    /// </summary>
    public interface IOutgoingGrainCallContext : IGrainCallContext
    {
        /// <summary>
        /// 获取发送方 Grain 的上下文。
        /// </summary>
        public IGrainContext? SourceContext { get; }
    }
}
