using Orleans.Durable.Tasks;
namespace Orleans.Durable.Http;

/// <summary>
/// 发送 HTTP 请求并运行 HTTP 操作,作为耐久任务的步骤, 因此任务重写和重放到达
/// 服务器侧面工作相同。
/// </summary>
/// <remarks>
/// <para>
/// 返回持久任务记录的方法,每一方法都作为调用任务的步骤,作为调用任务:重放读取
/// 记录了未再发送请求请求而没有再次发送的回复,以及故障或中断中断中断发送的一步
/// 请求中再次提出了与确定该步骤的同一 <c>Idempotency-Key</c>相同的请求。
/// <c>GET</c>使用这些方法,可以从耐用处决中(例如:可耐用死刑)使用。 grain 方法, 内 silos.
/// </para>
/// <para>
/// 业务遵循本一揽子方案持久运作终点服务的各项公约,这些公约与
/// Azure 长期运行业务: <c>202 Accepted</c> 答复将业务状况监测器定位为
/// <c>Operation-Location</c>,其中 <c>POST {status monitor}:cancel</c> 取消和
/// <c>POST {status monitor}/signals/{name}</c> 信号。 <see cref="WaitForOperationAsync{TResult}"/> 也等待
/// 运行的 Azure 服务操作和服务器,回答 <c>202 Accepted</c> ,位置为 <c>Location</c>。
/// </para>
/// <para>
/// 以 <c>AddDurableHttpClient</c>注册一个客户端,该客户端将返回其 <see cref="HttpClient"/>的构建者,并且
/// 以客户名作为密钥服务注入
/// </para>
/// </remarks>
public interface IDurableHttpClient
{
    /// <summary>发送 <paramref name="createRequest"/> 创建的请求,作为调用任务的一个步骤。</summary>
    /// <param name="createRequest">创建请求;它运行每个步骤的尝试。</param>
    /// <returns>一项持久任务, 以记录的答复( 以其状态代码为准) 返回所记录的答复。</returns>
    DurableTask<DurableHttpResponse> SendAsync(Func<HttpRequestMessage> createRequest);

    /// <summary>发送启动操作的请求,作为调用任务的一个步骤,并返回操作。</summary>
    /// <param name="createRequest">创建请求;它运行每个步骤的尝试。</param>
    /// <returns>一项持久任务, 返回运行, 启动响应可能已经完成的操作 。</returns>
    /// <exception cref="HttpRequestException">
    /// 答复既不报告一项行动,也不报告结果,或在未说明行动地点的情况下接受请求。
    /// </exception>
    DurableTask<DurableHttpOperation> StartOperationAsync(Func<HttpRequestMessage> createRequest);

    /// <summary>易腐性等待操作结果。</summary>
    /// <typeparam name="TResult">操作类型 JSON 后的结果使该结果的消散状态降低。</typeparam>
    /// <param name="operation">操作 <see cref="StartOperationAsync"/> 返回。</param>
    /// <param name="options">配置默认值的等待或 <see langword="null"/> 。</param>
    /// <returns>返回操作结果的持久任务。</returns>
    /// <remarks>
    /// <para>
    /// 启动回复, <see cref="DurableHttpOperation.IsCompleted"/> 报告产生结果,
    /// <c>204 No Content</c> 响应请假 <see langword="default"/>,或,与 <c>424 Failed Dependency</c>状态, 状态
    /// 状态监测报告 <c>status</c> ,一旦它结束,
    /// <c>result</c> 或 <c>error</c>; a <c>Location</c> 答解答复 <c>202 Accepted</c> 运行和运行时
    /// 一旦成功,结果就会产生。
    /// </para>
    /// <para>
    /// 民意调查的间隔 <c>Retry-After</c> 广告,或 <see cref="DurableHttpOperationOptions.PollInterval"/>,
    /// 每次民意调查记录了延误和一步,任务删除后删除了这一段,因此倾向于间隔。
    /// 适合操作预期时间或 <see cref="DurableHttpWaitMode.Events"/>。
    /// </para>
    /// </remarks>
    /// <exception cref="DurableHttpOperationException">操作失败或取消。</exception>
    /// <exception cref="HttpRequestException">答复中既无状况代码,也无结果报告。</exception>
    DurableTask<TResult> WaitForOperationAsync<TResult>(DurableHttpOperation operation, DurableHttpOperationOptions? options = null);

    /// <summary>读取操作状态监视器 作为调用任务的一个步骤</summary>
    /// <param name="operationLocation">运行状态监视器的 URL。</param>
    /// <returns>持久任务, 返回操作状态及其在结束时的结果或错误。</returns>
    /// <exception cref="HttpRequestException">响应没有成功状态代码。</exception>
    DurableTask<DurableOperation> GetOperationAsync(Uri operationLocation);

    /// <summary>要求取消 <c>POST {status monitor}:cancel</c>以作为任务的一个步骤</summary>
    /// <param name="operationLocation">运行状态监视器的 URL。</param>
    /// <param name="reason">操作任务为何读作,或 <see langword="null"/> 没有。</param>
    /// <returns>
    /// 一项持久的任务, 返回运行状态: 在取消收益时运行, 或者在终端 。
    /// </returns>
    /// <exception cref="HttpRequestException">响应没有成功状态代码。</exception>
    DurableTask<DurableOperation> CancelOperationAsync(Uri operationLocation, string? reason = null);

    /// <summary>
    /// 发送信号 JSON 与 <c>POST {status monitor}/signals/{name}</c>,作为
    /// 任务
    /// </summary>
    /// <typeparam name="T">信号值类型。</typeparam>
    /// <param name="operationLocation">运行状态监视器的 URL。</param>
    /// <param name="signalName">信号的名称。</param>
    /// <param name="value">信号值。</param>
    /// <returns>耐久任务,一旦操作持有信号即完成。</returns>
    /// <exception cref="HttpRequestException">
    /// 反应没有成功状态代码,例如 <c>409 Conflict</c> 用于进行一项具有另一
    /// 值或完成时没有信号。
    /// </exception>
    DurableTask SignalOperationAsync<T>(Uri operationLocation, string signalName, T value);

    /// <summary>
    /// 将操作状态从 <c>{status monitor}/events</c> 服务器上的事件中流出到运行前的运行状态
    /// 是终端。
    /// </summary>
    /// <param name="operationLocation">运行状态监视器的 URL。</param>
    /// <param name="cancellationToken">停止流。</param>
    /// <returns>运行状态,以其终端状态结束。</returns>
    /// <remarks>
    /// <para>
    /// 结束状态之前结束或下降的流流,或者无法连接到的服务器,在
    /// 服务器广告的间隔, 以及最后一个事件标识符, 作为 <c>EventSource</c> 连接
    /// 运行报告当前状态, 并重复最后状态 。
    /// </para>
    /// <para>
    /// 流流不是一个持久的步骤:在可持久处决之外阅读,例如请求处理者或
    /// 步骤, <see cref="DurableHttpWaitMode.Events"/> 做。
    /// </para>
    /// </remarks>
    /// <exception cref="HttpRequestException">响应没有成功状态代码。</exception>
    IAsyncEnumerable<DurableOperation> WatchOperationAsync(Uri operationLocation, CancellationToken cancellationToken = default);
}

/// <summary>包含一个 <see cref="IDurableHttpClient"/>的操作。</summary>
public static class DurableHttpClientExtensions
{
    /// <summary>
    /// 以 <paramref name="createRequest"/> 创建的请求启动操作,并长期等待
    /// 结果。
    /// </summary>
    /// <typeparam name="TResult">操作类型 JSON 后的结果使该结果的消散状态降低。</typeparam>
    /// <param name="client">客户。</param>
    /// <param name="createRequest">创建启动操作的请求;它运行为每次尝试的启动。</param>
    /// <param name="options">配置默认值的等待或 <see langword="null"/> 。</param>
    /// <returns>返回操作结果的持久任务。</returns>
    /// <exception cref="DurableHttpOperationException">操作失败或取消。</exception>
    /// <exception cref="HttpRequestException">答复既不报告一项行动,也不报告结果。</exception>
    public static DurableTask<TResult> RunOperationAsync<TResult>(
        this IDurableHttpClient client,
        Func<HttpRequestMessage> createRequest,
        DurableHttpOperationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(createRequest);
        return RunAsync(client, createRequest, options);

        static async DurableTask<TResult> RunAsync(IDurableHttpClient client, Func<HttpRequestMessage> createRequest, DurableHttpOperationOptions? options)
        {
            var operation = await client.StartOperationAsync(createRequest);
            return await client.WaitForOperationAsync<TResult>(operation, options);
        }
    }
}
