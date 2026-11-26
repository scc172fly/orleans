using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 登记框架拥有的持久工作处理人,以便启动 grain 。
/// </summary>
/// <remarks>
/// 登记是启动式范围广的,匹配功能处理程序优先于
/// <see cref="IDurableJobHandler"/>. 多个匹配的功能处理器以模糊性被拒绝 。
/// DurableJobs 依赖性注射登记是一种基础设施服务,不支持
/// 替换在 DurableJobs 配置或
/// 接收器已解决, 以便不能静悄悄地将登记与发送断开 。
/// </remarks>
public interface IDurableJobHandlerRegistry
{
    /// <summary>
    /// 登记登记册 <paramref name="handler"/> 为此 grain 启动
    /// </summary>
    /// <param name="handler">启动范围功能处理器。</param>
    /// <exception cref="InvalidOperationException">处理器已经注册。</exception>
    void Register(IDurableJobFeatureHandler handler);
}

/// <summary>
/// 处理框架拥有的耐久工作,以启动 grain 。
/// </summary>
/// <remarks>
/// 处理人确定持久的工作配置,并拥有任何
/// 完成的传票再次送达时,需要符合具体适用要求的适当性。
/// </remarks>
public interface IDurableJobFeatureHandler
{
    /// <summary>
    /// 确定该处理器是否处理所提供的耐久工作。
    /// </summary>
    /// <remarks>
    /// 实施必须具有确定性和无副作用,可以多次称为“方法”。
    /// 同一持久工作名称。
    /// </remarks>
    /// <param name="jobName">持久的职称。</param>
    /// <returns><see langword="true"/> 当该处理器处理该工作名称时; 否则, <see langword="false"/>。</returns>
    bool CanHandle(string jobName);

    /// <summary>
    /// 执行持久工作,并归还其明确处置。
    /// </summary>
    /// <param name="context">持久工作执行背景。</param>
    /// <param name="attemptCancellationToken">
    /// 合作要求取消目前执行企图的象征。
    /// 持久工作仍有资格再次尝试。
    /// </param>
    /// <returns>完成、投票或处理失败。</returns>
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
