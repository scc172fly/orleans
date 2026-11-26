using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Orleans.Durable.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>登记作为人道主义过渡工作队业务的持久任务所服务的服务。</summary>
public static class DurableHttpServiceCollectionExtensions
{
    /// <summary>
    /// 添加持久操作终端点使用的服务,
    /// <see cref="Microsoft.AspNetCore.Builder.DurableHttpEndpointRouteBuilderExtensions"/> 和
    /// <see cref="Microsoft.AspNetCore.Builder.NexusEndpointRouteBuilderExtensions"/> 地图。
    /// </summary>
    /// <param name="services">服务收藏,它也提供 Orleans <see cref="Orleans.IGrainFactory"/>。</param>
    /// <param name="configureOptions">配置操作,或默认的 <see langword="null"/> 。</param>
    /// <returns>服务收藏。</returns>
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
    /// 添加 <see cref="IDurableHttpClient"/>,该耐久任务将输入为
    /// <paramref name="name"/>,并返回它发送请求的命名 <see cref="HttpClient"/> 的建设者。
    /// </summary>
    /// <param name="services">下 silo服务收藏。</param>
    /// <param name="name">客户名,即服务键和 <see cref="HttpClient"/>的名称。</param>
    /// <param name="configureOptions">配置客户端,或默认值的 <see langword="null"/> 。</param>
    /// <returns>
    /// 名客户端的创建者,第一个处理者是 <see cref="DurableIdempotencyKeyHandler"/>,所以处理者
    /// 并且,建筑商(例如复原力处理者)会添加这些内容,用他们的钥匙重新研究请求。
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
    /// 添加 silos 发送回调的服务,其中要求以 <c>Operation-Callback</c>注册
    /// 头或 Nexus <c>callback</c>, 并返回交付它们的 <see cref="HttpClient"/> 的构建器。
    /// </summary>
    /// <param name="services">下 silo服务收藏。</param>
    /// <param name="configureOptions">配置交付,或默认的 <see langword="null"/> 。</param>
    /// <returns>
    /// 发送回馈的客户端创建器, 配置其处理器, 如代理服务器。 客户端
    /// 与未交付的重写 仅连接到地址
    /// <see cref="DurableCallbackOptions.AllowAddress"/> 接受。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 每一个回调都是一个中继器上的一项独立、耐久的任务 grain等待其行动的结果 并交付
    /// 含有耐久再试的中继重试。交付方描述失败时 <see cref="DurableHttpOptions.CreateError"/>,因此配置
    /// <see cref="DurableHttpOptions"/> 中在 silos 用于运行的东道主; 运行两个功能的东道主
    /// 共享一个配置。 注册一个 <see cref="IDurableWebhookSigner"/> 以签署 Webhooks 。
    /// </para>
    /// <para>
    /// 主机保护回回调所携带的页眉的值, 这可能具有 ASP.NET 核心数据
    /// 在 silos 储存之前保护,交付前保护,不保护它们,所以主机和 silos 共享数据保护
    /// 键和应用程序名称,例如 <c>AddDataProtection().PersistKeysTo...().SetApplicationName(...)</c>。
    /// </para>
    /// <para>
    /// 客户主控管器是 <see cref="SocketsHttpHandler"/>,其 <c>ConnectCallback</c> 适用
    /// <see cref="DurableCallbackOptions.AllowAddress"/>. 应用程序配置为
    /// <see cref="SocketsHttpHandler"/> 没有 a 中 <c>ConnectCallback</c>,或,当 <c>AllowAddress</c> 是
    /// <see langword="null"/>、 a <see cref="SocketsHttpHandler"/> 或 a <see cref="HttpClientHandler"/>; 构建客户端
    /// 使用另一个处理器丢弃 <see cref="InvalidOperationException"/>,因为软件包无法阻止它跟随
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
/// 防止回调客户端跟踪重定向,并避免连接到地址
/// <see cref="DurableCallbackOptions.AllowAddress"/> 不接受, 不论应用程序配置的主要处理者为何。
/// </summary>
/// <remarks>
/// 重定向, 或者主机名, 或主机名, 其解析到另一个地址时, 而不是应用程序接受 URL 时, 将会发送
/// 发送到接收器,但 <see cref="DurableHttpOptions.AllowCallback"/> 不接受。过滤器在每一个
/// 客户的处理器配置, 并且未能建立客户 其主处理器无法约束的客户。
/// </remarks>
internal sealed class DurableCallbackHandlerFilter(Microsoft.Extensions.Options.IOptions<DurableCallbackOptions> options) : IHttpMessageHandlerBuilderFilter
{
    /// <summary>默认主处理器打开给一个接收器的最大连接。</summary>
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
