using Orleans.Serialization.Invocation;

namespace Orleans.Runtime;

/// <summary>
/// grain 上下文通过 <see cref="IGrainContext.ReceiveLocalRequest"/> 收到并收到其答复的请求。
/// </summary>
/// <remarks>
/// grain 上下文通过收到的 grain 呼叫过滤器,援引启动请求
/// 对 <see cref="Complete"/>的响应, 然后处理请求。 呼叫过滤器、 日志和跟踪记录时, 观察请求的界面名称、 方法名称和参数 。
/// 请求书及其答复通过参考而不是复制,因此请求书与打电话者分享变体特征,同时自行复制。
/// </remarks>
public interface ILocalRequest : IInvokable
{
    /// <summary>
    /// 完成请求并附上结果。
    /// </summary>
    /// <param name="response">
    /// 请求及其呼叫过滤器作出的答复,或 grain 上下文拒绝请求的例外情况。
    /// grain 上下文拒绝在请求开始之前停止使用 <see cref="OrleansMessageRejectionException"/> 的请求。
    /// </param>
    /// <remarks>
    /// 请求书拥有其结果,包括报告或伐木失败,并接受收到的第一个结果。
    /// 这种方法可以从任何线索中调用,因此执行过程应迅速完成,且不得阻拦。
    /// </remarks>
    void Complete(Response response);
}
