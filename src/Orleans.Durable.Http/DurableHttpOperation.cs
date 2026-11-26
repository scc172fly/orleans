using System.Net;
using System.Text.Json;

namespace Orleans.Durable.Http;

/// <summary>确定一项HTTP操作,该操作是长期任务,始于 <see cref="IDurableHttpClient.StartOperationAsync"/>.</summary>
/// <param name="Location">
/// 报告操作的 URL 报告运行 : 其状态监视器, 或一个应答服务器的 <c>Location</c>
/// <c>202 Accepted</c> 直到它返回结果。它是 <see langword="null"/> 启动时的操作
/// 答复完成后没有回复。
/// </param>
/// <param name="IsStatusMonitor">
/// <paramref name="Location"/> 是状态监测器,它 <c>Operation-Location</c> 还是 <c>Azure-AsyncOperation</c>
/// 并报告操作 <c>status</c>、 <c>result</c>和 <c>error</c>的名称。
/// </param>
/// <param name="Response">启动操作的响应。</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableHttpOperation")]
public sealed record DurableHttpOperation(Uri? Location, bool IsStatusMonitor, DurableHttpResponse Response)
{
    /// <summary>了解启动响应是否完成操作,结果或失败。</summary>
    /// <remarks>
    /// A <c>202 Accepted</c> 答复报告正在运行,除情况监测器之外,任何有状况监测器的答复也报告正在进行中。
    /// <c>200 OK</c>、 <c>204 No Content</c>和 <c>424 Failed Dependency</c>,如 Azure <c>201 Created</c>
    /// 业务仍然提供的资源。
    /// </remarks>
    public bool IsCompleted => Response.StatusCode switch
    {
        HttpStatusCode.Accepted => false,
        HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.FailedDependency => true,
        _ => !IsStatusMonitor,
    };

    /// <summary>读取启动响应所识别的操作。</summary>
    internal static DurableHttpOperation FromStart(DurableHttpResponse response)
    {
        // 424 依赖关系报告一项行动因状态监测器故障或被取消而失败。
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.FailedDependency)
        {
            throw response.CreateException();
        }

        if (Locate(response, DurableHttpHeaders.OperationLocation) is { } monitor)
        {
            return new(monitor, IsStatusMonitor: true, response);
        }

        if (Locate(response, "Azure-AsyncOperation") is { } azureMonitor)
        {
            return new(azureMonitor, IsStatusMonitor: true, response);
        }

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            return new(Location: null, IsStatusMonitor: false, response);
        }

        // 地点标题仅以202个接受方答复中的名称命名业务;在201个创建方答复中,它指定
        // 创建的资源 。
        return new(
            Locate(response, "Location") ?? throw new HttpRequestException(
                $"'{response.RequestUri}' accepted the request without an Operation-Location, Azure-AsyncOperation, or Location header which locates its operation.",
                inner: null,
                response.StatusCode),
            IsStatusMonitor: false,
            response);

        static Uri? Locate(DurableHttpResponse response, string header) =>
            response.GetHeader(header) is { Length: > 0 } value
                ? response.RequestUri is { } requestUri ? new Uri(requestUri, value) : new Uri(value, UriKind.Absolute)
                : null;
    }
}

/// <summary>选择 <see cref="IDurableHttpClient.WaitForOperationAsync{TResult}"/> 如何观察运行中的操作。</summary>
public enum DurableHttpWaitMode
{
    /// <summary>
    /// 每一次民意调查都记录了一次拖延和一步,没有要求
    /// 民意调查之间保持开放,适合任何期限的业务。
    /// </summary>
    Poll,

    /// <summary>
    /// 读取操作状态监视器上的服务器发送事件 <c>{status monitor}/events</c>,在一步骤内
    /// 任务记录一步, 并尽快了解结果
    /// 服务器报告, 而请求在操作持续期间保持打开。 没有状态监视器的操作
    /// 被民意测验。
    /// </summary>
    Events,
}

/// <summary>配置耐久任务如何等待 HTTP 操作。</summary>
public sealed record DurableHttpOperationOptions
{
    internal static DurableHttpOperationOptions Default { get; } = new();

    /// <summary>获取任务如何观察运行中的操作。 默认值为 <see cref="DurableHttpWaitMode.Poll"/>。</summary>
    public DurableHttpWaitMode Mode { get; init; }

    /// <summary>
    /// 获得运行中操作的民意测验间隔间最短的时间间隔, 当响应不公布时使用这一间隔间隔间隔
    /// 以 <c>Retry-After</c>后再加一个较长的。默认为5秒。
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 获得两次投票之间最长的间隔, 从而将响应限制在 <c>Retry-After</c>。默认为一分钟,
    /// 短于 <see cref="PollInterval"/> 的间隔作为 <see cref="PollInterval"/>适用。
    /// </summary>
    public TimeSpan MaxPollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获得每次民意测验都用 <c>Prefer: wait</c> 页头,所以是一个服务器,长号回答一次
    /// 操作完成; <see cref="TimeSpan.Zero"/>,默认,无要求。
    /// </summary>
    public TimeSpan Wait { get; init; }
}

/// <summary>配置 <see cref="IDurableHttpClient"/>。</summary>
public sealed class DurableHttpClientOptions
{
    /// <summary>选项读取对未通过自传的呼叫者的答复;没有调用者配置这些响应。</summary>
    internal static JsonSerializerOptions WebSerializerOptions { get; } = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;

    /// <summary>
    /// 获取或设置将请求值序列化和取消结果的选项。默认值是
    /// ASP.NET 核心最小 API JSON 选项,用于每个客户端: <see cref="JsonSerializerDefaults.Web"/>,并按序
    /// 应用允许时, 反射。
    /// </summary>
    /// <remarks>
    /// 客户从选项 <see cref="JsonSerializerOptions.TypeInfoResolver"/>中解决每种类型的合同。
    /// 或 NativeAOT 应用程序设置了选项,其解决者可使用选项,例如源生成的
    /// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>,处理其请求和结果类型。
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; set; } = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;

    /// <summary>
    /// 获取或设置最大响应内容, 以字节表示, 一步读取并记录。 默认为一度中位节 。
    /// </summary>
    /// <remarks>
    /// 任务记录每个回复, 所以限制可以防止响应超过其日记存储在一个条目中的内容。 A
    /// 较大的反应失败了 <see cref="HttpRequestException"/>,任务记录 喜欢任何失败。
    /// </remarks>
    public long MaxResponseContentLength { get; set; } = 1024 * 1024;

    /// <summary>
    /// 获取或设置函数,以决定客户是否遵循启动响应定位的 URL 用户是否遵循启动响应
    /// ,根据启动请求的 URL 和操作的位置,在操作中进行。
    /// </summary>
    /// <remarks>
    /// 客户通过自己的主管人员的资格证明,向业务所在地发送民意测验、取消和信号。
    /// 默认的跟随启动请求源、 其方案、 主机和端口的位置, 仅此选项, 服务器无法
    /// 函数衰落的位置以
    /// <see cref="HttpRequestException"/>.
    /// </remarks>
    public Func<Uri, Uri, bool> FollowOperationLocation { get; set; } = IsSameOrigin;

    /// <summary>返回是否 <paramref name="location"/> 计划、主机和港口 <paramref name="requestUri"/>.</summary>
    /// <param name="requestUri">启动操作的请求的 URL。</param>
    /// <param name="location">操作地点。</param>
    /// <returns><see langword="true"/> 当两个 URL 都有一个来源时 。</returns>
    public static bool IsSameOrigin(Uri requestUri, Uri location)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(location);
        return Uri.Compare(requestUri, location, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}

/// <summary>表示 HTTP 操作失败或被取消。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableHttpOperationException")]
public sealed class DurableHttpOperationException : Exception
{
    private DurableHttpOperationException()
    {
    }

    /// <summary>开始对以 <paramref name="status"/>结束的行动作例外处理。</summary>
    /// <param name="status">业务的终点状态 <see cref="DurableOperationStatus.Failed"/> 或 <see cref="DurableOperationStatus.Canceled"/>.</param>
    /// <param name="code">操作报告的任何错误代码。</param>
    /// <param name="message">操作报告的任何错误信息。</param>
    public DurableHttpOperationException(DurableOperationStatus status, string? code, string? message)
        : base(string.IsNullOrEmpty(message) ? $"The operation ended as {status}." : message)
    {
        Status = status;
        Code = code;
    }

    /// <summary>获得操作终端状态。</summary>
    [Id(0)]
    public DurableOperationStatus Status { get; private set; }

    /// <summary>获取操作报告的任何错误代码。</summary>
    [Id(1)]
    public string? Code { get; private set; }
}
