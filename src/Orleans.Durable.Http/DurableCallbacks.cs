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

/// <summary>标识回调协议，该协议决定回调请求的请求头与请求体。</summary>
[GenerateSerializer]
public enum DurableCallbackProtocol
{
    /// <summary>
    /// 采用 Standard Webhooks 请求头的 Webhook，携带操作的状态监视器：<c>webhook-id</c>（在
    /// 各次尝试中相同）、<c>webhook-timestamp</c>，以及 <c>webhook-signature</c>（当
    /// <see cref="IDurableWebhookSigner"/> 为交付签名时发送）。
    /// </summary>
    Webhook,

    /// <summary>
    /// Nexus 完成回调，携带结果或 <c>Failure</c>，并附带 <c>Nexus-Operation-Token</c>、
    /// <c>Nexus-Operation-State</c> 和 <c>Nexus-Operation-Close-Time</c> 请求头。不发送
    /// <c>Nexus-Operation-Start-Time</c>，因此调用方使用收到完成通知的时间。
    /// </summary>
    Nexus,
}

/// <summary>描述向调用方 URL 交付操作结果的回调。</summary>
/// <param name="Protocol">回调请求的协议。</param>
/// <param name="OperationHost">承载操作的 Grain。</param>
/// <param name="OperationId">调用方用于识别操作的标识，即操作标识符或 Nexus 令牌。</param>
/// <param name="Url">接收回调的 URL。</param>
/// <param name="Headers">
/// 调用方要求每次交付携带的请求头，按小写名称索引。其值可能包含凭据，因此每个值均
/// 使用 ASP.NET Core Data Protection 进行保护，并在实际交付时解除保护。
/// </param>
/// <param name="OperationLocation">操作状态监视器的 URL，用于 Webhook。</param>
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
    /// <summary>保护回调请求头值的数据保护器的用途字符串。</summary>
    internal const string HeaderProtectionPurpose = "Orleans.Durable.Http.DurableCallback.Headers";

    /// <summary>
    /// 返回回调标识符：对用于定位交付目标的成员进行哈希计算，因此针对一个操作对同一
    /// 接收方进行的所有注册（例如重试启动时的注册）都会到达同一中继，不受所携带请求头影响。
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

/// <summary>按 Standard Webhooks 规范为 Webhook 交付签名。</summary>
/// <remarks>
/// 应用自行管理签名密钥：在 Silo 的服务集合中注册一个实现，例如使用各接收方专用的密钥
/// 计算 <c>v1,{base64(HMAC-SHA256(secret, "{id}.{timestamp}.{body}"))}</c>。签名器若
/// 抛出异常，本次交付尝试即失败，随后由交付机制重试。
/// </remarks>
public interface IDurableWebhookSigner
{
    /// <summary>返回本次交付的 <c>webhook-signature</c> 请求头值；若不发送该请求头，则返回 <see langword="null"/>。</summary>
    /// <param name="callback">本次交付所属的回调。</param>
    /// <param name="webhookId">本次交付的 <c>webhook-id</c>。</param>
    /// <param name="timestamp">本次交付尝试的 <c>webhook-timestamp</c>，以 Unix 纪元起的秒数表示。</param>
    /// <param name="body">本次交付的完整请求体字节。</param>
    string? Sign(DurableCallback callback, string webhookId, long timestamp, ReadOnlySpan<byte> body);
}

/// <summary>配置回调交付。</summary>
public sealed class DurableCallbackOptions
{
    /// <summary>用于交付回调的 <see cref="HttpClient"/> 的名称。</summary>
    public const string HttpClientName = "Orleans.Durable.Http.Callbacks";

    /// <summary>
    /// 获取或设置从开始交付起允许重试回调的时长，超过该时长则放弃交付。默认值为
    /// 三天。
    /// </summary>
    public TimeSpan RetryTimeout { get; set; } = TimeSpan.FromDays(3);

    /// <summary>获取或设置首次重试前的间隔，此后每次重试的间隔加倍。默认值为一秒。</summary>
    public TimeSpan InitialRetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设置重试之间的最长间隔，包括接收方通过 <c>Retry-After</c>
    /// 请求的间隔。默认值为五分钟。
    /// </summary>
    /// <remarks>
    /// 每次重试等待加倍后的间隔与接收方 <c>Retry-After</c> 所指定间隔中的较大值，但不超过本
    /// 配置的间隔，再增加最多四分之一的抖动时长，以分散发往同一接收方的大量回调重试。
    /// </remarks>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取或设置一个函数，用于判定交付时是否允许连接到回调 URL 的主机名在建立连接时
    /// 解析出的地址；若为 <see langword="null"/>，则将连接处理交由客户端的主
    /// 处理程序。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认实现 <see cref="IsPublicAddress"/> 只接受公有地址，因此当回调 URL 的主机名解析到
    /// 环回、私有、链路本地或其他内部地址时（包括应用接受 URL 后 DNS 解析结果发生变化的情况），
    /// 交付不会访问内部服务。实际交付连接到通过检查的
    /// 地址。
    /// </para>
    /// <para>
    /// 此检查在回调客户端的 <c>ConnectCallback</c>（属于 <see cref="SocketsHttpHandler"/>）中执行。若客户端
    /// 的主处理程序使用其他连接方式（例如 <see cref="HttpClientHandler"/>，或
    /// <see cref="SocketsHttpHandler"/>（自带 <c>ConnectCallback</c>）），则客户端构建会失败，除非将此属性设置为
    /// <see langword="null"/>.
    /// </para>
    /// </remarks>
    public Func<IPAddress, bool>? AllowAddress { get; set; } = IsPublicAddress;

    /// <summary>
    /// 返回 <paramref name="address"/> 是否为公有单播地址，而不是环回、私有、共享、
    /// 链路本地、多播、文档示例或其他保留地址。
    /// </summary>
    /// <param name="address">地址。</param>
    /// <returns><see langword="true"/> 表示公有单播地址。</returns>
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

            // 2002::/16（6to4）嵌入 IPv4 地址，64:ff9b::/96 用于转换 IPv4 地址，2001:db8::/32 用于文档示例。
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

/// <summary>表示已放弃交付的回调。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableCallbackDeliveryException")]
public sealed class DurableCallbackDeliveryException : Exception
{
    private DurableCallbackDeliveryException()
    {
    }

    /// <summary>为已放弃交付的回调初始化异常。</summary>
    /// <param name="message">最后一次尝试的说明。</param>
    public DurableCallbackDeliveryException(string message)
        : base(message)
    {
    }
}

/// <summary>操作进入终态后交付回调，直至接收方接受为止（必要时重试）。</summary>
[Alias("Orleans.Durable.Http.IDurableCallbackRelayGrain")]
internal interface IDurableCallbackRelayGrain : IGrainWithStringKey
{
    [Alias("Deliver")]
    DurableTask DeliverAsync(DurableCallback callback);
}

/// <summary>
/// 中继回调：每个回调都是在独立中继 Grain 上运行的分离式持久化任务，以回调标识符为键，
/// 等待操作进入终态，并通过持久化重试交付其结果。
/// </summary>
/// <remarks>
/// 交付采用至少一次语义：接收方根据 <c>webhook-id</c> 或 <c>Nexus-Operation-Token</c> 去重。
/// 接收方返回 <c>2xx</c> 或 <c>410 Gone</c> 时结束交付；否则可能以 <see cref="DurableCallbackDeliveryException"/> 失败，
/// 即 <see cref="DurableCallbackOptions.RetryTimeout"/> 到期时仍未成功；中继保留失败信息，用于诊断和日志记录。
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
    /// <summary>中继上交付任务的根任务标识符。</summary>
    public const string DeliveryRootId = "deliver";

    private readonly IDataProtector _headers = dataProtection.CreateProtector(DurableCallback.HeaderProtectionPurpose);

    public async DurableTask DeliverAsync(DurableCallback callback)
    {
        var options = callbackOptions.Value;
        // 订阅将操作的终态响应（结果保留原有类型）记录在此任务树中，并
        // 将逻辑时间推进至操作完成的时间。
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
    /// 返回下次尝试前的等待时长：取 <paramref name="backoff"/> 和接收方
    /// <paramref name="retryAfter"/> 所指定时长中的较大值，不超过 <paramref name="maxInterval"/>，再增加最多四分之一的抖动时长；该抖动根据
    /// 回调及尝试次数确定，因此每次重放的等待时长均相同。
    /// </summary>
    internal static TimeSpan GetRetryDelay(TimeSpan? retryAfter, TimeSpan backoff, TimeSpan maxInterval, string callbackId, int attemptNumber)
    {
        var delay = retryAfter > backoff ? retryAfter.Value : backoff;
        delay = delay < maxInterval ? delay : maxInterval;

        // 对回调标识符和尝试次数计算 FNV-1a 哈希。
        var hash = 2166136261u;
        foreach (var character in callbackId)
        {
            hash = (hash ^ character) * 16777619u;
        }

        hash = (hash ^ (uint)attemptNumber) * 16777619u;
        return delay + delay * (hash / (double)uint.MaxValue / 4);
    }

    /// <summary>返回用于交付操作终态 <paramref name="response"/> 的请求体。</summary>
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

    /// <summary>执行一次交付尝试，并返回接收方是否接受该交付。</summary>
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
            // 客户端超时。
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

/// <summary>将回调交付连接到 <see cref="DurableCallbackOptions.AllowAddress"/> 接受的地址。</summary>
internal static class DurableCallbackConnections
{
    /// <summary>
    /// 解析交付所连接的主机名，并连接到 <paramref name="allowAddress"/> 接受的第一个
    /// 地址，确保检查通过的地址就是实际交付到达的地址。
    /// </summary>
    /// <exception cref="HttpRequestException">主机名未解析到任何 <paramref name="allowAddress"/> 接受的地址。</exception>
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

/// <summary>记录用于交付操作终态结果的请求。</summary>
/// <param name="Body">请求体。</param>
/// <param name="State">操作的终态，采用回调协议中的状态名称。</param>
/// <param name="ClosedAt">操作完成的时间，以观察时记录的值为准。</param>
[GenerateSerializer, Immutable]
internal sealed record DurableCallbackOutcome(byte[] Body, string State, DateTimeOffset ClosedAt);

/// <summary>记录一次交付尝试。</summary>
/// <param name="Delivered">接收方是否已接受交付，或通过 <c>410 Gone</c> 停止该回调。</param>
/// <param name="Status">响应状态，或导致未获得响应的故障。</param>
/// <param name="RetryAfter">响应的 <c>Retry-After</c> 所指定的下次尝试前的间隔。</param>
[GenerateSerializer, Immutable]
internal sealed record DurableCallbackAttempt(bool Delivered, string Status, TimeSpan? RetryAfter);
