using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 为 Grain 激活实例注册框架拥有的持久化作业处理程序。
/// </summary>
/// <remarks>
/// 注册的作用域为激活实例。匹配的功能处理程序优先于
/// <see cref="IDurableJobHandler"/>。如果多个功能处理程序匹配，则因存在歧义而拒绝。
/// DurableJobs 的依赖注入注册属于基础设施服务，不支持
/// 替换或装饰。配置 DurableJobs 或解析接收方时会明确拒绝替换，
/// 以免已注册的处理程序在不知情的情况下与分派机制脱离。
/// </remarks>
public interface IDurableJobHandlerRegistry
{
    /// <summary>
    /// 为此 Grain 激活实例注册 <paramref name="handler"/>。
    /// </summary>
    /// <param name="handler">作用域为激活实例的功能处理程序。</param>
    /// <exception cref="InvalidOperationException">处理程序已注册。</exception>
    void Register(IDurableJobFeatureHandler handler);
}

/// <summary>
/// 为 Grain 激活实例处理框架拥有的持久化作业。
/// </summary>
/// <remarks>
/// 投递至少一次。处理程序决定持久化作业的处置结果，并负责
/// 已完成的调用再次投递时所需的应用特定幂等性处理。
/// </remarks>
public interface IDurableJobFeatureHandler
{
    /// <summary>
    /// 确定此处理程序是否处理指定的持久化作业。
    /// </summary>
    /// <remarks>
    /// 实现必须具有确定性且无副作用。对于同一个持久化作业名称，
    /// 此方法可能被调用多次。
    /// </remarks>
    /// <param name="jobName">持久化作业名称。</param>
    /// <returns><see langword="true"/> 表示此处理程序处理该作业名称；否则为 <see langword="false"/>。</returns>
    bool CanHandle(string jobName);

    /// <summary>
    /// 执行持久化作业并返回明确的处置结果。
    /// </summary>
    /// <param name="context">持久化作业执行上下文。</param>
    /// <param name="attemptCancellationToken">
    /// 用于协作式请求取消当前执行尝试的令牌。
    /// 持久化作业仍可再次尝试执行。
    /// </param>
    /// <returns>表示已完成、继续轮询或失败的处置结果。</returns>
    ValueTask<DurableJobRunResult> ExecuteJobAsync(IJobRunContext context, CancellationToken attemptCancellationToken);
}

internal interface IDurableJobHandlerLookup
{
    bool TryGetHandler(string jobName, [NotNullWhen(true)] out IDurableJobFeatureHandler? handler);
}

internal sealed class DurableJobHandlerRegistry : IDurableJobHandlerRegistry, IDurableJobHandlerLookup
{
    private readonly List<IDurableJobFeatureHandler> _handlers = [];

    public void Register(IDurableJobFeatureHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        foreach (var registeredHandler in _handlers)
        {
            if (ReferenceEquals(registeredHandler, handler))
            {
                throw new InvalidOperationException("The durable job feature handler is already registered.");
            }
        }

        _handlers.Add(handler);
    }

    public bool TryGetHandler(string jobName, [NotNullWhen(true)] out IDurableJobFeatureHandler? handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        handler = null;
        foreach (var candidate in _handlers)
        {
            if (!candidate.CanHandle(jobName))
            {
                continue;
            }

            if (handler is not null)
            {
                throw new InvalidOperationException(
                    $"Multiple durable job feature handlers match job '{jobName}': "
                    + $"'{handler.GetType().FullName}' and '{candidate.GetType().FullName}'.");
            }

            handler = candidate;
        }

        return handler is not null;
    }
}
