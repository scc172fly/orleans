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
/// Orleans 运行时使用的弹性策略。
/// </summary>
internal static class OrleansRuntimeResiliencePolicies
{
    /// <summary>
    /// 用于在 <see cref="Polly.Registry.ResiliencePipelineProvider{TKey}"/> 中标识放置弹性管道的键。
    /// </summary>
    public const string PlacementResiliencePipelineKey = "Orleans.Placement";

    /// <summary>
    /// 向服务集合添加全部 Orleans 运行时弹性策略。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合，用于链式调用。</returns>
    internal static IServiceCollection AddOrleansRuntimeResiliencePolicies(IServiceCollection services)
    {
        // 放置弹性管道。
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
    /// 确定异常是否为瞬时故障，以及是否应在放置操作期间重试。
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
