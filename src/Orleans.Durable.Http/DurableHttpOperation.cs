using System.Net;
using System.Text.Json;

namespace Orleans.Durable.Http;

/// <summary>标识持久化任务通过 <see cref="IDurableHttpClient.StartOperationAsync"/> 启动的 HTTP 操作。</summary>
/// <param name="Location">
/// 用于报告操作的 URL：可以是状态监视器的 URL，也可以是服务器返回的 <c>Location</c>（服务器在
/// 返回结果前一直响应 <c>202 Accepted</c>）。若启动响应已经完成操作且未提供此 URL，则为 <see langword="null"/>，
/// 即操作已在没有后续观察地址的情况下完成。
/// </param>
/// <param name="IsStatusMonitor">
/// 指示 <paramref name="Location"/> 是否为状态监视器，其地址由 <c>Operation-Location</c> 或 <c>Azure-AsyncOperation</c>
/// 指定，并报告操作的 <c>status</c>、<c>result</c> 和 <c>error</c>。
/// </param>
/// <param name="Response">启动操作时收到的响应。</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableHttpOperation")]
public sealed record DurableHttpOperation(Uri? Location, bool IsStatusMonitor, DurableHttpResponse Response)
{
    /// <summary>获取启动响应是否已完成操作（返回结果或失败）。</summary>
    /// <remarks>
    /// <c>202 Accepted</c> 响应表示操作仍在运行；带有状态监视器且状态码不是
    /// <c>200 OK</c>、<c>204 No Content</c> 或 <c>424 Failed Dependency</c> 的响应同样如此，例如 Azure 的 <c>201 Created</c>
    /// 响应（操作仍在预配对应资源）。
    /// </remarks>
    public bool IsCompleted => Response.StatusCode switch
    {
        HttpStatusCode.Accepted => false,
        HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.FailedDependency => true,
        _ => !IsStatusMonitor,
    };

    /// <summary>读取启动响应所标识的操作。</summary>
    internal static DurableHttpOperation FromStart(DurableHttpResponse response)
    {
        // 424 Failed Dependency 返回操作失败或被取消时的状态监视器。
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

        // Location 响应头仅在 202 Accepted 响应中标识操作；在 201 Created 响应中，它标识的是
        // 创建的资源。
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

/// <summary>选择 <see cref="IDurableHttpClient.WaitForOperationAsync{TResult}"/> 观察正在运行的操作的方式。</summary>
public enum DurableHttpWaitMode
{
    /// <summary>
    /// 轮询操作，在每次轮询之前进行持久化延迟。每次轮询记录一次延迟和一个步骤，且两次轮询之间
    /// 不保持请求连接，适合任意持续时长的操作。
    /// </summary>
    Poll,

    /// <summary>
    /// 在一个步骤中读取操作状态监视器的服务器发送事件，地址为 <c>{status monitor}/events</c>，
    /// 并持续重连直至操作进入终态。任务仅记录一个步骤，服务器报告结果时立即获知结果，
    /// 但请求在操作运行期间保持连接。没有状态监视器的操作
    /// 使用轮询方式观察。
    /// </summary>
    Events,
}

/// <summary>配置持久化任务等待 HTTP 操作的方式。</summary>
public sealed record DurableHttpOperationOptions
{
    internal static DurableHttpOperationOptions Default { get; } = new();

    /// <summary>获取任务观察正在运行的操作的方式。默认值为 <see cref="DurableHttpWaitMode.Poll"/>。</summary>
    public DurableHttpWaitMode Mode { get; init; }

    /// <summary>
    /// 获取轮询正在运行的操作的最短间隔；若响应未通过
    /// <c>Retry-After</c> 声明更长的间隔，则应用此值。默认值为五秒。
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 获取轮询之间的最长间隔，用于限制响应中的 <c>Retry-After</c>。默认值为一分钟，
    /// 若该值短于 <see cref="PollInterval"/>，则按 <see cref="PollInterval"/> 处理。
    /// </summary>
    public TimeSpan MaxPollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 获取每次轮询通过 <c>Prefer: wait</c> 请求头请求的等待时长，使支持长轮询的服务器在
    /// 操作完成时响应；默认值 <see cref="TimeSpan.Zero"/> 表示不请求等待。
    /// </summary>
    public TimeSpan Wait { get; init; }
}

/// <summary>配置 <see cref="IDurableHttpClient"/>。</summary>
public sealed class DurableHttpClientOptions
{
    /// <summary>调用方未传入自身选项时用于读取响应的选项；调用方不得对其进行配置。</summary>
    internal static JsonSerializerOptions WebSerializerOptions { get; } = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;

    /// <summary>
    /// 获取或设置序列化请求值及反序列化结果的选项。默认情况下为每个客户端创建一个新的
    /// ASP.NET Core Minimal API JSON 选项实例：<see cref="JsonSerializerDefaults.Web"/>，它在应用允许时使用
    /// 反射进行序列化。
    /// </summary>
    /// <remarks>
    /// 客户端通过选项中的 <see cref="JsonSerializerOptions.TypeInfoResolver"/> 解析各类型的契约。经过
    /// 裁剪的应用或 NativeAOT 应用应配置相应选项，使其解析器（例如源代码生成的
    /// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>）能够处理请求和结果类型。
    /// </remarks>
    public JsonSerializerOptions SerializerOptions { get; set; } = new Microsoft.AspNetCore.Http.Json.JsonOptions().SerializerOptions;

    /// <summary>
    /// 获取或设置一个步骤读取并记录的最大响应内容字节数。默认值为 1 MiB。
    /// </summary>
    /// <remarks>
    /// 任务记录每个响应，因此此限制可避免响应超过日志单条记录的存储容量。较大的
    /// 响应会使其步骤抛出 <see cref="HttpRequestException"/> 而失败，任务会像记录其他失败一样记录此异常。
    /// </remarks>
    public long MaxResponseContentLength { get; set; } = 1024 * 1024;

    /// <summary>
    /// 获取或设置一个函数，用于根据启动请求的 URL 与操作的位置，判定客户端是否跟随启动响应
    /// 指定的操作 URL。
    /// </summary>
    /// <remarks>
    /// 客户端将轮询、取消及信号请求连同其处理程序提供的凭据发送到操作的位置。
    /// 默认情况下仅跟随与启动请求同源（方案、主机和端口相同）的位置，因此服务器无法
    /// 将这些请求导向其他源。若函数拒绝该位置，则启动会因以下异常而失败：
    /// <see cref="HttpRequestException"/>.
    /// </remarks>
    public Func<Uri, Uri, bool> FollowOperationLocation { get; set; } = IsSameOrigin;

    /// <summary>返回 <paramref name="location"/> 的方案、主机及端口是否与 <paramref name="requestUri"/> 相同。</summary>
    /// <param name="requestUri">启动操作的请求的 URL。</param>
    /// <param name="location">操作的位置。</param>
    /// <returns><see langword="true"/> 表示两个 URL 同源。</returns>
    public static bool IsSameOrigin(Uri requestUri, Uri location)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(location);
        return Uri.Compare(requestUri, location, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}

/// <summary>表示已失败或被取消的 HTTP 操作。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableHttpOperationException")]
public sealed class DurableHttpOperationException : Exception
{
    private DurableHttpOperationException()
    {
    }

    /// <summary>为以 <paramref name="status"/> 结束的操作初始化异常。</summary>
    /// <param name="status">操作的终态，<see cref="DurableOperationStatus.Failed"/> 或 <see cref="DurableOperationStatus.Canceled"/>。</param>
    /// <param name="code">操作报告的错误码（如果有）。</param>
    /// <param name="message">操作报告的错误消息（如果有）。</param>
    public DurableHttpOperationException(DurableOperationStatus status, string? code, string? message)
        : base(string.IsNullOrEmpty(message) ? $"The operation ended as {status}." : message)
    {
        Status = status;
        Code = code;
    }

    /// <summary>获取操作的终态。</summary>
    [Id(0)]
    public DurableOperationStatus Status { get; private set; }

    /// <summary>获取操作报告的错误码（如果有）。</summary>
    [Id(1)]
    public string? Code { get; private set; }
}
