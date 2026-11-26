using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>配置耐久任务使用的 HTTP 客户端。</summary>
public static class DurableHttpClientBuilderExtensions
{
    /// <summary>
    /// 添加a <see cref="DurableIdempotencyKeyHandler"/>,其中每个请求都发出一项持久任务的请求
    /// <c>Idempotency-Key</c> 确定任务, 委托人的处理者。
    /// </summary>
    /// <param name="builder">HTTP 客户端构建器,位于一个 silo ,可执行耐久任务。</param>
    /// <returns>HTTP 客户端构建器。</returns>
    /// <remarks>在添加重新处理请求的操作器之前先拨打此方法, 这样他们的重试会随键而移动 。</remarks>
    public static IHttpClientBuilder AddDurableIdempotencyKey(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddTransient<DurableIdempotencyKeyHandler>();
        return builder.AddHttpMessageHandler<DurableIdempotencyKeyHandler>();
    }
}
