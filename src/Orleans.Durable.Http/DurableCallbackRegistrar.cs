using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Http;

/// <summary>读取启动操作和启动继电器的请求的回溯登记。</summary>
internal sealed class DurableCallbackRegistrar(IGrainFactory grainFactory, IDataProtectionProvider dataProtection, IOptions<DurableHttpOptions> options)
{
    private readonly IDataProtector _headers = dataProtection.CreateProtector(DurableCallback.HeaderProtectionPurpose);

    /// <summary>交付文件本身设置或连接信头的名称和名称前缀。</summary>
    private static readonly string[] ReservedHeaders =
        ["host", "connection", "keep-alive", "te", "trailer", "transfer-encoding", "upgrade", "expect", "operation-location"];

    private static readonly string[] ReservedHeaderPrefixes = ["proxy-", "content-", "webhook-", "nexus-operation-"];

    /// <summary>
    /// 读取请求登记册所使用的回调 <paramref name="url"/> 和页眉,以
    /// <paramref name="headerPrefix"/>,当应用程序不时返回 <see langword="false"/> 并附加描述
    /// 接受它。
    /// </summary>
    /// <remarks>
    /// 交付货物携带注册信头作为请求信头,因此,一个标有连接的页头,例如
    /// <c>Host</c>,它将发送到另一个服务器发送发送文件,而不是应用程序所接受的或
    /// 交付装置本身,如 <c>webhook-signature</c>等,不接受。
    /// </remarks>
    /// <param name="context">请求。</param>
    /// <param name="url">回调 URL,或 <see langword="null"/> 当请求没有登记时。</param>
    /// <param name="headerPrefix">每一交付货物携带的页眉的前缀,没有前缀。</param>
    /// <param name="registration">登记,或 <see langword="null"/> 当请求没有登记时。</param>
    /// <param name="error">应用程序不接受的回调描述。</param>
    public bool TryRead(
        HttpContext context,
        string? url,
        string headerPrefix,
        out DurableCallbackRegistration? registration,
        out string? error)
    {
        registration = null;
        error = null;
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            error = "A callback URL is an absolute http or https URL.";
            return false;
        }

        if (options.Value.AllowCallback is not { } allow || !allow(context, uri))
        {
            error = "This server doesn't accept the callback URL.";
            return false;
        }

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, values) in context.Request.Headers)
        {
            if (name.Length > headerPrefix.Length && name.StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var deliveryHeader = name[headerPrefix.Length..].ToLowerInvariant();
                if (Array.IndexOf(ReservedHeaders, deliveryHeader) >= 0
                    || Array.Exists(ReservedHeaderPrefixes, prefix => deliveryHeader.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    error = $"A callback doesn't carry the '{deliveryHeader}' header, which addresses the connection or which deliveries set themselves.";
                    return false;
                }

                headers[deliveryHeader] = _headers.Protect(values.ToString());
            }
        }

        registration = new(uri, headers);
        return true;
    }

    /// <summary>
    /// 启动中继,向注册的 <paramref name="host"/> 主机提供操作结果
    /// 重复注册接收器,例如重试启动程序, 到达相同的中继, 保持
    /// 第一次注册的页眉。
    /// </summary>
    public async ValueTask RegisterAsync(
        DurableCallbackRegistration registration,
        DurableCallbackProtocol protocol,
        GrainId host,
        string operationId,
        string? operationLocation,
        CancellationToken cancellationToken)
    {
        var callback = new DurableCallback(
            protocol,
            host,
            operationId,
            registration.Url,
            registration.Headers,
            operationLocation);
        try
        {
            await grainFactory.GetGrain<IDurableCallbackRelayGrain>(callback.GetCallbackId())
                .DeliverAsync(callback)
                .WithId(DurableCallbackRelayGrain.DeliveryRootId).ScheduleAsync(cancellationToken);
        }
        catch (DurableTaskConflictException)
        {
            // 接收器的登记较早时开始中继。
            // 登记,所以继电器保留了第一个登记
        }
    }
}

/// <summary>请求登记册的回调。</summary>
/// <param name="Url">收到回调的 URL。</param>
/// <param name="Headers">每件交货的页头都受到保护,其价值得到保护。</param>
internal sealed record DurableCallbackRegistration(Uri Url, IReadOnlyDictionary<string, string> Headers);
