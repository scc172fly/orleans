using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 为管理日志持久化状态的 Grain 提供便捷方法。
/// </summary>
public abstract class DurableGrain : Grain, IGrainBase
{
    /// <summary>
    /// 初始化 <see cref="DurableGrain"/> 类的新实例，并解析其状态管理器。
    /// </summary>
    /// <remarks>
    /// 标准状态管理器在绑定 Grain 的构造期间加入 Grain 生命周期。
    /// </remarks>
    protected DurableGrain()
    {
        StateManager = ServiceProvider.GetRequiredService<IDurableStateManager>();
    }

    /// <summary>
    /// 获取此 Grain 激活实例的日志状态管理器。
    /// </summary>
    protected IDurableStateManager StateManager { get; }

    /// <summary>
    /// 写入已注册的日志状态。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示写入操作的 <see cref="ValueTask"/>。</returns>
    protected ValueTask WriteStateAsync(CancellationToken cancellationToken = default) => StateManager.WriteStateAsync(cancellationToken);
}
