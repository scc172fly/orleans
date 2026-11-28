using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks;
using Orleans.Runtime;

namespace Orleans.Durable.Http;

/// <summary>读取启动操作的请求中注册的回调，并启动相应的中继。</summary>
internal sealed class DurableCallbackRegistrar(IGrainFactory grainFactory, IDataProtectionProvider dataProtection, IOptions<DurableHttpOptions> options)
{
    private readonly IDataProtector _headers = dataProtection.CreateProtector(DurableCallback.HeaderProtectionPurpose);

    /// <summary>交付过程自行设置的请求头以及涉及连接的请求头的名称和名称前缀。</summary>
    private static readonly string[] ReservedHeaders =
        ["host", "connection", "keep-alive", "te", "trailer", "transfer-encoding", "upgrade", "expect", "operation-location"];

    private static readonly string[] ReservedHeaderPrefixes = ["proxy-", "content-", "webhook-", "nexus-operation-"];

    /// <summary>
    /// 读取请求通过 <paramref name="url"/> 注册的回调及以下列前缀开头的请求头：
    /// <paramref name="headerPrefix"/>。若应用不接受该回调，则返回 <see langword="false"/>，
    /// 并给出原因说明。
    /// </summary>
    /// <remarks>
    /// 交付时将注册的请求头作为实际请求头发送，因此涉及连接的请求头（如
    /// <c>Host</c>，可能使交付发往应用接受的 URL 之外的服务器），以及
    /// 交付过程自行设置的请求头（如 <c>webhook-signature</c>）均不被接受。
    /// </remarks>
    /// <param name="context">请求。</param>
    /// <param name="url">回调 URL；若请求未注册回调，则为 <see langword="null"/>。</param>
    /// <param name="headerPrefix">每次交付携带的请求头的名称前缀；实际交付时去掉此前缀。</param>
    /// <param name="registration">回调注册信息；若请求未注册回调，则为 <see langword="null"/>。</param>
    /// <param name="error">对应用不接受的回调的说明。</param>
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
    /// 启动中继，将 <paramref name="host"/> 承载的操作的结果交付给已注册的
    /// 回调。对同一接收方重复注册（例如重试启动请求）会到达同一个中继，
    /// 该中继保留首次注册的请求头。
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
            // 接收方先前的注册已启动中继。各次注册中经过保护的请求头值可能不同，
            // 因此中继保留首次注册的值。
        }
    }
}

/// <summary>请求注册的回调。</summary>
/// <param name="Url">接收回调的 URL。</param>
/// <param name="Headers">每次交付携带的请求头，其值均受到保护。</param>
internal sealed record DurableCallbackRegistration(Uri Url, IReadOnlyDictionary<string, string> Headers);
