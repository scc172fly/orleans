using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

/// <summary>
/// 通过 <see cref="IGrainContext.ReceiveLocalRequest"/> 由 Grain 上下文接收，并接收自身响应的请求。
/// </summary>
/// <remarks>
/// Grain 上下文在激活实例上通过传入 Grain 调用过滤器执行此请求，将
/// 响应传递给 <see cref="Complete"/>，随后释放请求。调用过滤器、日志和跟踪可以观察请求的接口名称、方法名称及参数。
/// 请求和响应均按引用传递而不是复制，因此，与调用方共享可变状态的请求必须自行复制该状态。
/// </remarks>
public interface ILocalRequest : IInvokable
{
    /// <summary>
    /// 使用请求的执行结果完成该请求。
    /// </summary>
    /// <param name="response">
    /// 请求及其调用过滤器生成的响应；如果 Grain 上下文拒绝请求，则为异常。
    /// 如果 Grain 上下文在请求开始前停用，它将以 <see cref="OrleansMessageRejectionException"/> 拒绝该请求。
    /// </param>
    /// <remarks>
    /// 请求拥有其执行结果，负责报告或记录失败，并接受首次收到的结果。
    /// 此方法可以从任何线程调用，因此实现应快速完成且不阻塞。
    /// </remarks>
    void Complete(Response response);
}
