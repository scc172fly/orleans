using Orleans.Durable.Tasks;
namespace Orleans.Durable.Http;

/// <summary>
/// 将 HTTP 请求和 HTTP 操作作为持久化任务的步骤执行，使任务的重试和重放到达
/// 服务器端的同一工作。
/// </summary>
/// <remarks>
/// <para>
/// 每个返回持久化任务的方法均将其响应记录为调用任务的步骤：重放读取
/// 已记录的响应，无需重新发送请求；如果步骤因故障或停用而中断，则使用
/// 相同的 <c>Idempotency-Key</c> 重新发送请求，该键用于标识步骤。使用安全方法的请求，例如
/// <c>GET</c> 不携带键。应从 Silo 中的持久化执行实例（例如持久化 Grain 方法）调用这些方法。
/// </para>
/// <para>
/// 操作遵循本包持久化操作端点所提供的约定，该约定与
/// Azure 长时间运行操作兼容：<c>202 Accepted</c> 响应通过
/// <c>Operation-Location</c> 指定操作状态监视器的位置；<c>POST {status monitor}:cancel</c> 取消操作，
/// <c>POST {status monitor}/signals/{name}</c> 向操作发送信号。<see cref="WaitForOperationAsync{TResult}"/> 也可以等待
/// Azure 服务所运行的操作，以及返回 <c>202 Accepted</c> 且带有 <c>Location</c> 的服务器所运行的操作。
/// </para>
/// <para>
/// 使用 <c>AddDurableHttpClient</c> 注册客户端，该方法返回其 <see cref="HttpClient"/> 的构建器，并
/// 以客户端名称作为键注入该键控服务。
/// </para>
/// </remarks>
public interface IDurableHttpClient
{
    /// <summary>将 <paramref name="createRequest"/> 创建的请求作为调用任务的步骤发送。</summary>
    /// <param name="createRequest">创建请求；在步骤的每次尝试中运行。</param>
    /// <returns>返回已记录响应的持久化任务，不受响应状态码限制。</returns>
    DurableTask<DurableHttpResponse> SendAsync(Func<HttpRequestMessage> createRequest);

    /// <summary>将启动操作的请求作为调用任务的步骤发送，并返回该操作。</summary>
    /// <param name="createRequest">创建请求；在步骤的每次尝试中运行。</param>
    /// <returns>返回操作的持久化任务，该操作可能已由启动响应完成。</returns>
    /// <exception cref="HttpRequestException">
    /// 响应既未报告操作也未报告结果，或接受请求但未给出操作位置。
    /// </exception>
    DurableTask<DurableHttpOperation> StartOperationAsync(Func<HttpRequestMessage> createRequest);

    /// <summary>以持久化方式等待操作结果。</summary>
    /// <typeparam name="TResult">操作的 JSON 结果反序列化后得到的类型。</typeparam>
    /// <param name="operation"><see cref="StartOperationAsync"/> 返回的操作。</param>
    /// <param name="options">配置等待；为 <see langword="null"/> 时使用默认配置。</param>
    /// <returns>返回操作结果的持久化任务。</returns>
    /// <remarks>
    /// <para>
    /// 若 <see cref="DurableHttpOperation.IsCompleted"/> 表明启动响应已完成操作，则该响应携带结果；其中
    /// <c>204 No Content</c> 响应的结果为 <see langword="default"/>，或者 <c>424 Failed Dependency</c> 响应携带失败或取消操作的状态
    /// 监视器。状态监视器报告 <c>status</c>，并在进入终态后报告
    /// <c>result</c> 或 <c>error</c>；通过 <c>Location</c> 指定的地址在操作运行期间响应 <c>202 Accepted</c>，并在操作
    /// 成功后返回结果。
    /// </para>
    /// <para>
    /// 轮询等待 <c>Retry-After</c> 声明的间隔或 <see cref="DurableHttpOperationOptions.PollInterval"/>，
    /// 采用持久化延迟。每次轮询记录一个延迟和一个步骤，删除任务时会删除这些记录，因此优先选择
    /// 适合操作预期时长的间隔，或使用 <see cref="DurableHttpWaitMode.Events"/>。
    /// </para>
    /// </remarks>
    /// <exception cref="DurableHttpOperationException">操作失败或被取消。</exception>
    /// <exception cref="HttpRequestException">响应状态码既未报告状态也未报告结果。</exception>
    DurableTask<TResult> WaitForOperationAsync<TResult>(DurableHttpOperation operation, DurableHttpOperationOptions? options = null);

    /// <summary>将读取操作状态监视器作为调用任务的步骤。</summary>
    /// <param name="operationLocation">操作的状态监视器 URL。</param>
    /// <returns>返回操作状态的持久化任务；操作进入终态后还返回结果或错误。</returns>
    /// <exception cref="HttpRequestException">响应的状态码不是成功状态码。</exception>
    DurableTask<DurableOperation> GetOperationAsync(Uri operationLocation);

    /// <summary>将通过 <c>POST {status monitor}:cancel</c> 请求取消操作作为调用任务的步骤。</summary>
    /// <param name="operationLocation">操作的状态监视器 URL。</param>
    /// <param name="reason">操作任务读取的取消原因；无原因时为 <see langword="null"/>。</param>
    /// <returns>
    /// 返回操作状态的持久化任务：取消正在进行时可能仍为运行状态，或者已进入终态。
    /// </returns>
    /// <exception cref="HttpRequestException">响应的状态码不是成功状态码。</exception>
    DurableTask<DurableOperation> CancelOperationAsync(Uri operationLocation, string? reason = null);

    /// <summary>
    /// 将通过 <c>POST {status monitor}/signals/{name}</c> 向操作传递带 JSON 值的信号作为
    /// 调用任务的步骤。
    /// </summary>
    /// <typeparam name="T">信号的值类型。</typeparam>
    /// <param name="operationLocation">操作的状态监视器 URL。</param>
    /// <param name="signalName">信号的名称。</param>
    /// <param name="value">信号的值。</param>
    /// <returns>操作保存信号后即完成的持久化任务。</returns>
    /// <exception cref="HttpRequestException">
    /// 响应状态码不是成功状态码，例如 <c>409 Conflict</c> 表示操作已保存另一个
    /// 值，或操作完成时未收到该信号。
    /// </exception>
    DurableTask SignalOperationAsync<T>(Uri operationLocation, string signalName, T value);

    /// <summary>
    /// 从 <c>{status monitor}/events</c> 的服务器发送事件中以流式方式读取操作状态，直至操作
    /// 进入终态。
    /// </summary>
    /// <param name="operationLocation">操作的状态监视器 URL。</param>
    /// <param name="cancellationToken">停止事件流。</param>
    /// <returns>操作的状态序列，以终态结束。</returns>
    /// <remarks>
    /// <para>
    /// 若流在终态前结束或断开，或者服务器不可达，则等待服务器声明的间隔后
    /// 携带最后一个事件的标识符进行重连，与 <c>EventSource</c> 的行为相同。重连会
    /// 报告操作当前状态，该状态可能与最后一个状态相同。
    /// </para>
    /// <para>
    /// 此流不是持久化步骤：应在持久化执行实例之外读取，例如在请求处理程序中，或在
    /// 一个步骤中读取（<see cref="DurableHttpWaitMode.Events"/> 即采用这种方式）。
    /// </para>
    /// </remarks>
    /// <exception cref="HttpRequestException">响应状态码不是成功状态码。</exception>
    IAsyncEnumerable<DurableOperation> WatchOperationAsync(Uri operationLocation, CancellationToken cancellationToken = default);
}

/// <summary>组合 <see cref="IDurableHttpClient"/> 的操作。</summary>
public static class DurableHttpClientExtensions
{
    /// <summary>
    /// 使用 <paramref name="createRequest"/> 创建的请求启动操作，并以持久化方式等待其
    /// 结果。
    /// </summary>
    /// <typeparam name="TResult">操作的 JSON 结果反序列化后得到的类型。</typeparam>
    /// <param name="client">客户端。</param>
    /// <param name="createRequest">创建启动操作的请求；在每次启动尝试中运行。</param>
    /// <param name="options">配置等待；为 <see langword="null"/> 时使用默认配置。</param>
    /// <returns>返回操作结果的持久化任务。</returns>
    /// <exception cref="DurableHttpOperationException">操作失败或被取消。</exception>
    /// <exception cref="HttpRequestException">响应既未报告操作也未报告结果。</exception>
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
