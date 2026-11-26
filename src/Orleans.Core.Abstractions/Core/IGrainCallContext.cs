using System.Reflection;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

namespace Orleans
{
    /// <summary>
    /// 代表曾拦截援引请求。
    /// </summary>
    /// <param name="context">援引的背景。</param>
    /// <returns>A <see cref="Task"/> 必须在等待之后再继续处理。</returns>
    public delegate Task GrainCallFilterDelegate(IGrainCallContext context);

    /// <summary>
    /// 一位代表曾拦截一项收到的请求。
    /// </summary>
    /// <param name="context">援引的背景。</param>
    /// <returns>A <see cref="Task"/> 必须在等待之后再继续处理。</returns>
    public delegate Task OutgoingGrainCallFilterDelegate(IOutgoingGrainCallContext context);

    /// <summary>
    /// 一位代表曾拦截一项外向请求。
    /// </summary>
    /// <param name="context">援引的背景。</param>
    /// <returns>A <see cref="Task"/> 必须在等待之后再继续处理。</returns>
    public delegate Task IncomingGrainCallFilterDelegate(IIncomingGrainCallContext context);

    /// <summary>
    /// 指一种方法的援引以及援引的结果。
    /// </summary>
    public interface IGrainCallContext
    {
        /// <summary>
        /// 得到请求。
        /// </summary>
        IInvokable Request { get; }

        /// <summary>
        /// 获取所引用的 grain 。
        /// </summary>
        object Grain { get; }

        /// <summary>
        /// 如果有源代码,则获取源代码的身份。
        /// </summary>
        GrainId? SourceId { get; }

        /// <summary>
        /// 获得目标的身份
        /// </summary>
        GrainId TargetId { get; }

        /// <summary>
        /// 获取被引用的界面类型 。
        /// </summary>
        GrainInterfaceType InterfaceType { get; }

        /// <summary>
        /// 获取被引用的界面的名称 。
        /// </summary>
        string InterfaceName { get; }

        /// <summary>
        /// 获取被引用的方法的名称 。
        /// </summary>
        string MethodName { get; }

        /// <summary>
        /// 获取所引用界面方法的 <see cref="MethodInfo"/> 。
        /// </summary>
        MethodInfo InterfaceMethod { get; }

        /// <summary>
        /// 获取或设定结果。
        /// </summary>
        object? Result { get; set; }

        /// <summary>
        /// 获取或设置响应。
        /// </summary>
        Response? Response { get; set; }

        /// <summary>
        /// 提出请求。
        /// </summary>
        /// <returns>
        /// 以 <see cref="Task"/> 表示的祈祷。
        /// </returns>
        Task Invoke();
    }

    /// <summary>
    /// 表示一种新到的方法的调试和调试结果。
    /// </summary>
    public interface IIncomingGrainCallContext : IGrainCallContext
    {
        /// <summary>
        /// 获取目标的 grain 上下文。
        /// </summary>
        public IGrainContext TargetContext { get; }

        /// <summary>
        /// 获取所援引的执行方法的 <see cref="MethodInfo"/> 。
        /// </summary>
        MethodInfo ImplementationMethod { get; }
    }

    /// <summary>
    /// 表示即将使用的方法的调试和调试的结果。
    /// </summary>
    public interface IOutgoingGrainCallContext : IGrainCallContext
    {
        /// <summary>
        /// 获取发件人的 grain 上下文。
        /// </summary>
        public IGrainContext? SourceContext { get; }
    }
}
