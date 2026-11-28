using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Orleans.Durable.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>注册将持久化任务作为 HTTP 操作提供服务所需的服务。</summary>
public static class DurableHttpServiceCollectionExtensions
{
    /// <summary>
    /// 添加持久化操作端点所使用的服务，这些端点由
    /// <see cref="Microsoft.AspNetCore.Builder.DurableHttpEndpointRouteBuilderExtensions"/> 和
    /// <see cref="Microsoft.AspNetCore.Builder.NexusEndpointRouteBuilderExtensions"/> 映射。
    /// </summary>
    /// <param name="services">服务集合，其中还提供 Orleans 的 <see cref="Orleans.IGrainFactory"/>。</param>
    /// <param name="configureOptions">配置操作；为 <see langword="null"/> 时使用默认配置。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddDurableHttp(this IServiceCollection services, Action<DurableHttpOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<DurableHttpOptions>();
        if (configureOptions is not null)
        {
            options.Configure(configureOptions);
        }

        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DurableOperationService>();
        services.TryAddSingleton<DurableCallbackRegistrar>();
        services.TryAddSingleton<DurableHttpOperations>();
        services.TryAddSingleton<INexusOperationTokenFormat, PlainTextNexusOperationTokenFormat>();
        services.TryAddSingleton<DurableNexusOperations>();
        return services;
    }

    /// <summary>
    /// 添加一个 <see cref="IDurableHttpClient"/>，持久化任务以命名的键控服务形式注入它，
    /// 服务名称为 <paramref name="name"/>；并返回用于发送请求的命名 <see cref="HttpClient"/> 的构建器。
    /// </summary>
    /// <param name="services">Silo 的服务集合。</param>
    /// <param name="name">客户端名称，用作服务键并用于命名 <see cref="HttpClient"/>。</param>
    /// <param name="configureOptions">配置客户端；为 <see langword="null"/> 时使用默认配置。</param>
    /// <returns>
    /// 命名客户端的构建器，其首个处理程序是 <see cref="DurableIdempotencyKeyHandler"/>，因此
    /// 通过构建器添加的处理程序（例如弹性处理程序）会在重试请求时携带幂等键。
    /// </returns>
    /// <example>
    /// <code>
    /// services.AddDurableHttpClient("payments")
    ///     .ConfigureHttpClient(client =&gt; client.BaseAddress = new("https://payments.example"))
    ///     .AddStandardResilienceHandler();
    ///
    /// public sealed class OrderGrain([FromKeyedServices("payments")] IDurableHttpClient payments) : DurableGrain, IOrderGrain
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddDurableHttpClient(this IServiceCollection services, string name, Action<DurableHttpClientOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var options = services.AddOptions<DurableHttpClientOptions>(name);
        if (configureOptions is not null)
        {
            options.Configure(configureOptions);
        }

        services.TryAddKeyedSingleton<IDurableHttpClient>(name, static (provider, key) =>
        {
            var name = (string)key!;
            return new DurableHttpClient(
                provider.GetRequiredService<IHttpClientFactory>(),
                name,
                provider.GetRequiredService<IOptionsMonitor<DurableHttpClientOptions>>().Get(name));
        });
        return services.AddHttpClient(name).AddDurableIdempotencyKey();
    }

    /// <summary>
    /// 添加 Silo 交付回调所需的服务；请求通过 <c>Operation-Callback</c>
    /// 请求头或 Nexus <c>callback</c> 注册回调，并返回交付回调的 <see cref="HttpClient"/> 的构建器。
    /// </summary>
    /// <param name="services">Silo 的服务集合。</param>
    /// <param name="configureOptions">配置交付；为 <see langword="null"/> 时使用默认配置。</param>
    /// <returns>
    /// 用于交付回调的客户端的构建器，可配置其处理程序（例如代理）。该客户端
    /// 不跟随重定向，而是将其作为未交付的情况重试，并且只连接到
    /// <see cref="DurableCallbackOptions.AllowAddress"/> 接受的地址。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 每个回调都是中继 Grain 上的分离式持久化任务，等待操作的结果并通过
    /// 持久化重试进行交付。交付使用 <see cref="DurableHttpOptions.CreateError"/> 描述失败，因此应在
    /// Silo 中配置 <see cref="DurableHttpOptions"/>，与提供操作服务的宿主保持一致；同时承担两种角色的宿主
    /// 共享同一配置。注册 <see cref="IDurableWebhookSigner"/> 以签名 Webhook。
    /// </para>
    /// <para>
    /// 在 Silo 存储回调请求头值（可能包含凭据）之前，宿主使用 ASP.NET Core Data
    /// Protection 保护这些值，交付时再解除保护。因此宿主与 Silo 应共享 Data Protection
    /// 密钥环和应用名称，例如使用 <c>AddDataProtection().PersistKeysTo...().SetApplicationName(...)</c>。
    /// </para>
    /// <para>
    /// 客户端的主处理程序为 <see cref="SocketsHttpHandler"/>，其 <c>ConnectCallback</c> 应用
    /// <see cref="DurableCallbackOptions.AllowAddress"/>。应用自行配置的主处理程序可以是
    /// <see cref="SocketsHttpHandler"/>（不带 <c>ConnectCallback</c>），或者在 <c>AllowAddress</c> 为
    /// <see langword="null"/> 时，使用 <see cref="SocketsHttpHandler"/> 或 <see cref="HttpClientHandler"/>；若使用其他处理程序构建客户端，
    /// 则抛出 <see cref="InvalidOperationException"/>，因为此包无法阻止它跟随
    /// 重定向或连接到内部地址。
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder AddDurableHttpCallbacks(this IServiceCollection services, Action<DurableCallbackOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<DurableCallbackOptions>();
        if (configureOptions is not null)
        {
            options.Configure(configureOptions);
        }

        services.AddOptions<DurableHttpOptions>();
        services.AddDataProtection();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, DurableCallbackHandlerFilter>());
        return services.AddHttpClient(DurableCallbackOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { MaxConnectionsPerServer = DurableCallbackHandlerFilter.MaxConnectionsPerServer });
    }
}

/// <summary>
/// 无论应用配置何种主处理程序，均阻止回调客户端跟随重定向以及连接到
/// <see cref="DurableCallbackOptions.AllowAddress"/> 未接受的地址。
/// </summary>
/// <remarks>
/// 重定向或主机名在应用接受 URL 后解析到不同地址，可能导致
/// 交付发送给 <see cref="DurableHttpOptions.AllowCallback"/> 未接受的接收方。此筛选器在每次
/// 客户端处理程序配置完成后运行；若无法约束其主处理程序，则客户端构建失败。
/// </remarks>
internal sealed class DurableCallbackHandlerFilter(Microsoft.Extensions.Options.IOptions<DurableCallbackOptions> options) : IHttpMessageHandlerBuilderFilter
{
    /// <summary>默认主处理程序为单个接收方打开的最大连接数。</summary>
    internal const int MaxConnectionsPerServer = 16;

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name != DurableCallbackOptions.HttpClientName)
        {
            return;
        }

        var allowAddress = options.Value.AllowAddress;
        switch (builder.PrimaryHandler)
        {
            case SocketsHttpHandler sockets when sockets.ConnectCallback is null && allowAddress is not null:
                sockets.AllowAutoRedirect = false;
                sockets.ConnectCallback = (context, cancellationToken) =>
                    DurableCallbackConnections.ConnectAsync(context.DnsEndPoint, allowAddress, cancellationToken);
                break;
            case SocketsHttpHandler sockets when allowAddress is null:
                sockets.AllowAutoRedirect = false;
                break;
            case HttpClientHandler client when allowAddress is null:
                client.AllowAutoRedirect = false;
                break;
            default:
                throw new InvalidOperationException(
                    $"The callback client's primary handler, '{builder.PrimaryHandler.GetType()}', can't be kept from following redirects or connecting to addresses which DurableCallbackOptions.AllowAddress doesn't accept. Use a SocketsHttpHandler without a ConnectCallback, or set AllowAddress to null to leave connections to the handler.");
        }
    };
}
