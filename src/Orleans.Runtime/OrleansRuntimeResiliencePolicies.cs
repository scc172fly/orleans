using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Placement;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Orleans.Runtime;

/// <summary>
/// Orleans 运行时使用的复原力政策。
/// </summary>
internal static class OrleansRuntimeResiliencePolicies
{
    /// <summary>
    /// 用于确定 <see cref="Polly.Registry.ResiliencePipelineProvider{TKey}"/>中安置复原力管道的关键。
    /// </summary>
    public const string PlacementResiliencePipelineKey = "Orleans.Placement";

    /// <summary>
    /// 将所有 Orleans 运行时恢复政策添加到服务收藏中。
    /// </summary>
    /// <param name="services">服务收藏。</param>
    /// <returns>链锁服务收藏。</returns>
    internal static IServiceCollection AddOrleansRuntimeResiliencePolicies(IServiceCollection services)
    {
        // 安置复原力管道
        services.AddResiliencePipeline<string, SiloAddress>(PlacementResiliencePipelineKey, static (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<SiloMessagingOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILogger<PlacementService>>();
            builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();

            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.PlacementTimeout,
                OnTimeout = args =>
                {
                    logger.LogWarning("Grain placement operation timed out after {Timeout}.", options.PlacementTimeout);
                    return default;
                }
            });

            if (options.PlacementMaxRetries > 0)
            {
                builder.AddRetry(new RetryStrategyOptions<SiloAddress>
                {
                    MaxRetryAttempts = options.PlacementMaxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = options.PlacementRetryBaseDelay,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder<SiloAddress>().Handle<Exception>(ex => IsTransientPlacementException(ex)),
                    OnRetry = args =>
                    {
                        logger.LogDebug(args.Outcome.Exception, "Retrying grain placement operation. Attempt: {AttemptNumber}, Delay: {RetryDelay}.", args.AttemptNumber, args.RetryDelay);
                        return default;
                    }
                });
            }
        });

        return services;
    }

    /// <summary>
    /// 确定例外是否短暂,在安置作业期间应重新审理。
    /// </summary>
    private static bool IsTransientPlacementException(Exception exception) =>
        exception switch
        {
            OrleansException => true,
            TimeoutException => true,
            OperationCanceledException => false,
            _ => false
        };
}
