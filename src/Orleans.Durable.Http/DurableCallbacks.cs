using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Durable.Journaling;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Orleans.Durable.Http;

/// <summary>确定回调程序,由它决定其请求的页眉和机体。</summary>
[GenerateSerializer]
public enum DurableCallbackProtocol
{
    /// <summary>
    /// 带有标准 Webhoooks 头目: <c>webhook-id</c>的网络监视器,
    /// 凡企图者,都相等; <c>webhook-timestamp</c>,和 <c>webhook-signature</c> 时
    /// <see cref="IDurableWebhookSigner"/> 信号发送。
    /// </summary>
    Webhook,

    /// <summary>
    /// Nexus 完成反馈, 产生结果或效果 <c>Failure</c>,与 <c>Nexus-Operation-Token</c>,
    /// <c>Nexus-Operation-State</c>和 <c>Nexus-Operation-Close-Time</c> 页眉。它略去
    /// <c>Nexus-Operation-Start-Time</c>,因此打电话者使用收到完成的时间。
    /// </summary>
    Nexus,
}

/// <summary>描述向调用者的 URL 发送操作结果的回调。</summary>
/// <param name="Protocol">召回请求的规程</param>
/// <param name="OperationHost">操作所在的 grain 。</param>
/// <param name="OperationId">打电话者知道操作的识别符号:其操作识别符或Nexus 质证。</param>
/// <param name="Url">收到回调的 URL。</param>
/// <param name="Headers">
/// 调用者要求每批交付时的页头携带的页眉, 以小写字母名称表示。 其值可以是证书, 所以每个
/// 使用 ASP.NET 核心数据保护来保护价值,交付不保护其价值。
/// </param>
/// <param name="OperationLocation">运行状态监视器的 URL , 用于 Webhoooks 。</param>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableCallback")]
public sealed record DurableCallback(
    [property: Id(0)] DurableCallbackProtocol Protocol,
    [property: Id(1)] GrainId OperationHost,
    [property: Id(2)] string OperationId,
    [property: Id(3)] Uri Url,
    [property: Id(4)] IReadOnlyDictionary<string, string> Headers,
    [property: Id(5)] string? OperationLocation)
{
    /// <summary>保护回溯者价值的数据保护保护工具的宗旨。</summary>
    internal const string HeaderProtectionPurpose = "Orleans.Durable.Http.DurableCallback.Headers";

    /// <summary>
    /// 返回回调的识别符号:成员处理其交付的散列,因此每个成员的登记
    /// 操作( 如重审启动) 的接收器, 到达一个中继, 以信头随身携带 。
    /// </summary>
    internal string GetCallbackId()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in (ReadOnlySpan<string>)[Protocol.ToString(), OperationHost.ToString(), OperationId, Url.AbsoluteUri, OperationLocation ?? string.Empty])
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return WebEncoders.Base64UrlEncode(hash.GetHashAndReset());
    }
}

/// <summary>信号网络钩交付,按标准 Webhooks 键。</summary>
/// <remarks>
/// 应用程序拥有签名密钥:在 silos服务收藏中登记执行,例如
/// 计算 <c>v1,{base64(HMAC-SHA256(secret, "{id}.{timestamp}.{body}"))}</c> ,每个接收器都有一个秘密。
/// 投出失败的运送尝试, 交付重试。
/// </remarks>
public interface IDurableWebhookSigner
{
    /// <summary>返回 <c>webhook-signature</c> 交付货物的页头值,或 <see langword="null"/> 至于任何人,</summary>
    /// <param name="callback">交付的回调所属。</param>
    /// <param name="webhookId">送货 <c>webhook-id</c>.</param>
    /// <param name="timestamp">运送企图 <c>webhook-timestamp</c>,在自Unix时代后几秒钟。</param>
    /// <param name="body">送货尸体,字节为字节</param>
    string? Sign(DurableCallback callback, string webhookId, long timestamp, ReadOnlySpan<byte> body);
}

/// <summary>配置回传发送。</summary>
public sealed class DurableCallbackOptions
{
    /// <summary>发送回溯的 <see cref="HttpClient"/> 的名称。</summary>
    public const string HttpClientName = "Orleans.Durable.Http.Callbacks";

    /// <summary>
    /// 获取或设定发件时间多久, 从发件开始到放弃之前的回调。 默认值是
    /// 三天后
    /// </summary>
    public TimeSpan RetryTimeout { get; set; } = TimeSpan.FromDays(3);

    /// <summary>获取或设置第一次重试之前的间隔, 后一次重试的间隔会加倍。 默认为一秒钟 。</summary>
    public TimeSpan InitialRetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设定在重试之间最长的间隔,包括接收器 <c>Retry-After</c>
    /// 请求。 默认时间为5分钟。
    /// </summary>
    /// <remarks>
    /// 每次重试等待的间隔时间越长, 最多要等待双倍间隔的时间长度和接收器的 <c>Retry-After</c>, 最多要等待
    /// 间距,再加四分之一以上, 将许多回调的重试分到一个接收器。
    /// </remarks>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或设置函数,以决定发送是否连接到一个地址,该地址是回调 URL 主机
    /// 确定交付连接时的解决方案,或 <see langword="null"/> 将连接保留到客户主机
    /// 处理器
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认值 <see cref="IsPublicAddress"/>,仅接受公共地址,因此,一个回调的 URL,其主机对
    /// 反馈、私人、本地链接或其他内部地址,包括通过DNS(在
    /// 应用程序接受了 URL , 无法到达内部服务。 发送文件连接到检查所在地址
    /// 已接受。
    /// </para>
    /// <para>
    /// 检查运行 <c>ConnectCallback</c> 调回客户端的 <see cref="SocketsHttpHandler"/>客户
    /// 其主要处理器将其他方式连接起来,例如 <see cref="HttpClientHandler"/> 或
    /// <see cref="SocketsHttpHandler"/> 拥有自有 <c>ConnectCallback</c>,除非该财产是
    /// <see langword="null"/>.
    /// </para>
    /// </remarks>
    public Func<IPAddress, bool>? AllowAddress { get; set; } = IsPublicAddress;

    /// <summary>
    /// 返回是否 <paramref name="address"/> 是一个公共的单人广播地址,而不是回过头来、私人、共享和分享的,
    /// 链接-本地、多台、文档或其他保留文件。
    /// </summary>
    /// <param name="address">地址。</param>
    /// <returns><see langword="true"/> 用于公开的单人地址。</returns>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            address.TryWriteBytes(bytes, out _);
            return bytes switch
            {
                [0, ..] or [10, ..] or [127, ..] => false,
                [100, >= 64 and <= 127, ..] => false,
                [169, 254, ..] => false,
                [172, >= 16 and <= 31, ..] => false,
                [192, 0, 0 or 2, ..] or [192, 168, ..] => false,
                [198, 18 or 19, ..] or [198, 51, 100, _] or [203, 0, 113, _] => false,
                [>= 224, ..] => false,
                _ => true,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || address.IsIPv6UniqueLocal || address.IsIPv6Teredo || address.IsIPv4MappedToIPv6)
            {
                return false;
            }

            Span<byte> bytes = stackalloc byte[16];
            address.TryWriteBytes(bytes, out _);

            // 2002年:16(6至4) 嵌入IPv4地址,64:ff9b:96翻译1;2001:db8:32文件实例。
            return bytes switch
            {
                [0x20, 0x02, ..] => false,
                [0x00, 0x64, 0xff, 0x9b, ..] => false,
                [0x20, 0x01, 0x0d, 0xb8, ..] => false,
                _ => true,
            };
        }

        return false;
    }
}

/// <summary>表示发送放弃的回调。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableCallbackDeliveryException")]
public sealed class DurableCallbackDeliveryException : Exception
{
    private DurableCallbackDeliveryException()
    {
    }

    /// <summary>初始化了交付放弃的回调的例外。</summary>
    /// <param name="message">描述最后一次尝试。</param>
    public DurableCallbackDeliveryException(string message)
        : base(message)
    {
    }
}

/// <summary>一旦其运行为终端, 将发送回调, 重试直到接收者接受它。</summary>
[Alias("Orleans.Durable.Http.IDurableCallbackRelayGrain")]
internal interface IDurableCallbackRelayGrain : IGrainWithStringKey
{
    [Alias("Deliver")]
    DurableTask DeliverAsync(DurableCallback callback);
}

/// <summary>
/// 中继回回调:每次回回回都是由回回的识别符号作为键键,由自上继中继的单独、耐久的任务。
/// 等待操作终端结果,并用耐久重试来交付。
/// </summary>
/// <remarks>
/// 交付至少一次:接收器通过 <c>webhook-id</c> 或 <c>Nexus-Operation-Token</c>进行重复处理。
/// 接收者回答时结束 <c>2xx</c> 或 <c>410 Gone</c>,或失败 <see cref="DurableCallbackDeliveryException"/>
/// 一旦 <see cref="DurableCallbackOptions.RetryTimeout"/> 折叠,继电器保留用于诊断和日志。
/// </remarks>
[GrainType("orleans-durable-http-callback")]
internal sealed partial class DurableCallbackRelayGrain(
    IGrainFactory grainFactory,
    IHttpClientFactory httpClients,
    IDataProtectionProvider dataProtection,
    IServiceProvider services,
    IOptions<DurableCallbackOptions> callbackOptions,
    IOptions<DurableHttpOptions> httpOptions,
    IOptions<HttpJsonOptions> jsonOptions,
    ILogger<DurableCallbackRelayGrain> logger) : DurableGrain, IDurableCallbackRelayGrain
{
    /// <summary>其中继交付任务根标识符。</summary>
    public const string DeliveryRootId = "deliver";

    private readonly IDataProtector _headers = dataProtection.CreateProtector(DurableCallback.HeaderProtectionPurpose);

    public async DurableTask DeliverAsync(DurableCallback callback)
    {
        var options = callbackOptions.Value;
        // 订阅记录了业务的终端反应,其结果保留了任务类型,记录在这项任务的树上,以及
        // 向行动完成时的逻辑时间预付款。
        var subscription = await grainFactory.GetGrain(callback.OperationHost)
            .GetDurableTask<object?>(DurableOperationGrain.RootId)
            .WithId("observe")
            .ScheduleAsync();
        var response = DurableOperationService.Unwrap(await subscription.GetResponseAsync());
        DurableCallbackOutcome outcome;
        try
        {
            outcome = Format(callback, response, closedAt: DurableTask.Current.UtcNow);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            LogFormatFailed(logger, exception, callback.Url, callback.OperationId);
            throw new DurableCallbackDeliveryException($"The outcome of operation '{callback.OperationId}' couldn't be formatted for '{callback.Url}': {exception.Message}");
        }

        var deadline = DurableTask.Current.UtcNow + options.RetryTimeout;
        var retryInterval = options.InitialRetryInterval;
        for (var attemptNumber = 1; ; attemptNumber++)
        {
            var attempt = await DurableTask.Run(
                static (state, cancellationToken) => state.relay.SendAsync(state.callback, state.outcome, cancellationToken),
                (relay: this, callback, outcome));
            if (attempt.Delivered)
            {
                return;
            }

            var retryAt = DurableTask.Current.UtcNow + GetRetryDelay(attempt.RetryAfter, retryInterval, options.MaxRetryInterval, this.GetPrimaryKeyString(), attemptNumber);
            if (retryAt > deadline)
            {
                LogGaveUp(logger, callback.Url, callback.OperationId, attemptNumber, attempt.Status);
                throw new DurableCallbackDeliveryException(
                    $"The callback to '{callback.Url}' wasn't delivered within {options.RetryTimeout}; the last attempt received {attempt.Status}.");
            }

            await DurableTask.DelayUntil(retryAt);
            retryInterval = retryInterval * 2 < options.MaxRetryInterval ? retryInterval * 2 : options.MaxRetryInterval;
        }
    }

    /// <summary>
    /// 返回下次尝试前的延迟时间: <paramref name="backoff"/> 的长度和接收器的
    /// <paramref name="retryAfter"/>,最多 <paramref name="maxInterval"/>,加上最多不超过四分之一以上,其来源是
    /// 重复和尝试, 所以每次重播都等待同样的延迟。
    /// </summary>
    internal static TimeSpan GetRetryDelay(TimeSpan? retryAfter, TimeSpan backoff, TimeSpan maxInterval, string callbackId, int attemptNumber)
    {
        var delay = retryAfter > backoff ? retryAfter.Value : backoff;
        delay = delay < maxInterval ? delay : maxInterval;

        // FNV-1a 翻过回击识别码和尝试号
        var hash = 2166136261u;
        foreach (var character in callbackId)
        {
            hash = (hash ^ character) * 16777619u;
        }

        hash = (hash ^ (uint)attemptNumber) * 16777619u;
        return delay + delay * (hash / (double)uint.MaxValue / 4);
    }

    /// <summary>返回交付操作终端的请答机构 <paramref name="response"/>.</summary>
    private DurableCallbackOutcome Format(DurableCallback callback, DurableTaskResponse response, DateTimeOffset closedAt)
    {
        var json = jsonOptions.Value.SerializerOptions;
        if (callback.Protocol == DurableCallbackProtocol.Nexus)
        {
            var body = response.Status == DurableTaskStatus.CompletedSuccessfully
                ? response.ResultType is { } type ? JsonSerializer.SerializeToUtf8Bytes(response.Result, json.GetTypeInfo(type)) : []
                : JsonSerializer.SerializeToUtf8Bytes(DurableOperationResponses.ToNexusFailure(response, httpOptions.Value), NexusJsonContext.Default.NexusFailure);
            return new(body, DurableOperationResponses.GetNexusState(response), closedAt);
        }

        var operation = DurableOperationResponses.ToOperation(callback.OperationId, response, json, httpOptions.Value);
        return new(JsonSerializer.SerializeToUtf8Bytes(operation, DurableHttpJsonContext.Default.DurableOperation), operation.Status.ToString(), closedAt);
    }

    /// <summary>发送一次送货尝试,并返回接收者是否接受。</summary>
    private async Task<DurableCallbackAttempt> SendAsync(DurableCallback callback, DurableCallbackOutcome outcome, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, callback.Url) { Content = new ByteArrayContent(outcome.Body) };
        if (outcome.Body.Length > 0)
        {
            request.Content.Headers.ContentType = new("application/json");
        }

        try
        {
            foreach (var (name, value) in callback.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, _headers.Unprotect(value));
            }
        }
        catch (CryptographicException exception)
        {
            LogAttemptFailed(logger, exception, callback.Url, callback.OperationId);
            return new(false, "the callback's headers couldn't be unprotected, so the silo doesn't share the host's Data Protection keys", null);
        }

        if (callback.Protocol == DurableCallbackProtocol.Nexus)
        {
            request.Headers.TryAddWithoutValidation(NexusProtocol.OperationToken, callback.OperationId);
            request.Headers.TryAddWithoutValidation(NexusProtocol.OperationState, outcome.State);
            request.Headers.TryAddWithoutValidation(NexusProtocol.OperationCloseTime, outcome.ClosedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture));
        }
        else
        {
            var webhookId = this.GetPrimaryKeyString();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            request.Headers.TryAddWithoutValidation("webhook-id", webhookId);
            request.Headers.TryAddWithoutValidation("webhook-timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
            string? signature;
            try
            {
                signature = services.GetService<IDurableWebhookSigner>()?.Sign(callback, webhookId, timestamp, outcome.Body);
            }
            catch (Exception exception)
            {
                LogAttemptFailed(logger, exception, callback.Url, callback.OperationId);
                return new(false, $"the signer failed: {exception.Message}", null);
            }

            if (signature is not null)
            {
                request.Headers.TryAddWithoutValidation("webhook-signature", signature);
            }

            if (callback.OperationLocation is { } location)
            {
                request.Headers.TryAddWithoutValidation(DurableHttpHeaders.OperationLocation, location);
            }
        }

        try
        {
            using var response = await httpClients.CreateClient(DurableCallbackOptions.HttpClientName).SendAsync(request, cancellationToken);
            var delivered = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Gone;
            var retryAfter = response.Headers.RetryAfter is { } header
                ? header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null)
                : null;
            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            if (!delivered)
            {
                LogAttemptRefused(logger, callback.Url, callback.OperationId, status);
            }

            return new(delivered, status, retryAfter > TimeSpan.Zero ? retryAfter : null);
        }
        catch (HttpRequestException exception)
        {
            LogAttemptFailed(logger, exception, callback.Url, callback.OperationId);
            return new(false, exception.Message, null);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 客户的超时时间已经过去了
            LogAttemptFailed(logger, exception, callback.Url, callback.OperationId);
            return new(false, exception.Message, null);
        }
    }

    [LoggerMessage(1, LogLevel.Debug, "The callback to '{Url}' for operation '{OperationId}' wasn't delivered; the receiver answered {Status}. Delivery retries it.")]
    private static partial void LogAttemptRefused(ILogger logger, Uri url, string operationId, string status);

    [LoggerMessage(2, LogLevel.Information, "The callback to '{Url}' for operation '{OperationId}' wasn't delivered. Delivery retries it.")]
    private static partial void LogAttemptFailed(ILogger logger, Exception exception, Uri url, string operationId);

    [LoggerMessage(3, LogLevel.Warning, "Delivery gave up on the callback to '{Url}' for operation '{OperationId}' after {Attempts} attempts; the last attempt received {Status}.")]
    private static partial void LogGaveUp(ILogger logger, Uri url, string operationId, int attempts, string status);

    [LoggerMessage(4, LogLevel.Error, "The outcome of operation '{OperationId}' couldn't be formatted for the callback to '{Url}', so delivery gave up. Configure the JSON options of the silos to serialize the operation's result type.")]
    private static partial void LogFormatFailed(ILogger logger, Exception exception, Uri url, string operationId);
}

/// <summary>将回溯交付连接到 <see cref="DurableCallbackOptions.AllowAddress"/> 接受的地址。</summary>
internal static class DurableCallbackConnections
{
    /// <summary>
    /// 确定交付文件连接到第一个地址并连接到该地址的东道主 <paramref name="allowAddress"/>
    /// 接受,所以检查所接受的地址是交付到达的地址。
    /// </summary>
    /// <exception cref="HttpRequestException">主机解决的地址没有 <paramref name="allowAddress"/> 接受。</exception>
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, Func<IPAddress, bool> allowAddress, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        var allowed = Array.FindAll(addresses, address => allowAddress(address));
        if (allowed.Length == 0)
        {
            throw new HttpRequestException($"The callback host '{endpoint.Host}' resolves to no address which deliveries connect to.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>记录提供业务终端结果的请求。</summary>
/// <param name="Body">请求书的尸体</param>
/// <param name="State">操作的终端状态, 以回调协议的名称命名 。</param>
/// <param name="ClosedAt">观察记录了这次行动完成时的情况。</param>
[GenerateSerializer, Immutable]
internal sealed record DurableCallbackOutcome(byte[] Body, string State, DateTimeOffset ClosedAt);

/// <summary>记录一次运送未遂。</summary>
/// <param name="Delivered">接收人接受交货还是退出回调 <c>410 Gone</c>.</param>
/// <param name="Status">反应状态,或防止失败的失败。</param>
/// <param name="RetryAfter">下一次尝试之前答复 <c>Retry-After</c> 请求之后重新查询的间隔。</param>
[GenerateSerializer, Immutable]
internal sealed record DurableCallbackAttempt(bool Delivered, string Status, TimeSpan? RetryAfter);
