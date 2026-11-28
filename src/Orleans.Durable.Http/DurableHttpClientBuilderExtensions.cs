using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>配置持久化任务使用的 HTTP 客户端。</summary>
public static class DurableHttpClientBuilderExtensions
{
    /// <summary>
    /// 向客户端的处理程序链添加 <see cref="DurableIdempotencyKeyHandler"/>，使持久化任务的每个请求均携带
    /// 用于标识该任务的 <c>Idempotency-Key</c>。
    /// </summary>
    /// <param name="builder">运行持久化任务的 Silo 中的 HTTP 客户端构建器。</param>
    /// <returns>HTTP 客户端构建器。</returns>
    /// <remarks>应在添加重试请求的处理程序之前调用此方法，使其重试也携带幂等键。</remarks>
    public static IHttpClientBuilder AddDurableIdempotencyKey(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddTransient<DurableIdempotencyKeyHandler>();
        return builder.AddHttpMessageHandler<DurableIdempotencyKeyHandler>();
    }
}
