namespace Orleans.Runtime;

/// <summary>
/// 打开独家部分, 保留激活请求的回源值, 而代码运行在激活上 。
/// </summary>
/// <remarks>
/// 激活程序执行 <c>IGrainContext.GetComponent</c> 返回的部件。
/// 启动程序表上调用。
/// 任何请求: grain 留存性、 <see cref="Concurrency.AlwaysInterleaveAttribute"/> 和
/// <see cref="Concurrency.ReentrantAttribute"/>、 <see cref="Concurrency.MayInterleaveAttribute"/> 的上游,只读
/// <see cref="EnterExclusiveSectionAsync"/> 表示在开放部分时接纳。
/// </remarks>
internal interface IGrainRequestAdmission
{
    /// <summary>
    /// 打开在激活调度器上运行的代码的独家区域,例如交易。而该区域则打开了该区
    /// 启动启动启动请求,总是中断,并请求部分持有人的呼叫链;
    /// 其他请求等待该部分结束。
    /// </summary>
    /// <param name="cancellationToken">取消打开部分关闭的等待。</param>
    /// <returns>
    /// 区域, 一旦没有其它专栏打开, 则该部分将关闭一次。 科将同时打开一个
    /// 要求的顺序。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 持牌者的呼叫链是调用码 <see cref="RequestContext.ReentrancyId"/>,该方法读作
    /// 同步, 在它返回之前。 一个等待的区块会保留代码中的呼叫链, 并保存请求它和命令的调用链 。
    /// 挂牌者允许的电线链回回回回
    /// 因此,<see cref="RequestContext.AllowCallChainReentrancy"/>,因此,在部分内运行而不是陷入僵局。
    /// 持有者,其可留存身份为 <see cref="Guid.Empty"/> 的持有人没有呼叫链,因此当其部分打开时激活
    /// 仅启动总是中断的请求 。
    /// </para>
    /// <para>
    /// 某部分不等待已经运行的请求, 所以从 grain 方法等待此部分的呼叫者不会等待
    /// 等待它。在非返回 grain上,一个 grain 方法的一节因此会搁置所有其他请求,除非
    /// 常以内道和被召于火狱的,在一队归依者之上。 grain和不运行在
    /// 请求,在打开部分时已经正在处理的请求可以继续同时进行。
    /// </para>
    /// <para>
    /// 打开的区段计为活动, 所以闲置收藏在打开时会保持激活。 取消的等待投
    /// <see cref="OperationCanceledException"/> 并放弃在顺序中的位置。当激活时等待的区块
    /// 以 <see cref="InvalidOperationException"/>清除断层的尾部。
    /// </para>
    /// </remarks>
    ValueTask<IDisposable> EnterExclusiveSectionAsync(CancellationToken cancellationToken);
}
