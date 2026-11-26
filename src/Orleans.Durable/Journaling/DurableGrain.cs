using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 为管理日记耐用状态的 grains 提供方便方法。
/// </summary>
public abstract class DurableGrain : Grain, IGrainBase
{
    /// <summary>
    /// 初始化了 <see cref="DurableGrain"/> 级的新实例,解决了国家经理的问题。
    /// </summary>
    /// <remarks>
    /// 国家标准管理人员注册 grain 使用期间 grain- 开工
    /// </remarks>
    protected DurableGrain()
    {
        StateManager = ServiceProvider.GetRequiredService<IDurableStateManager>();
    }

    /// <summary>
    /// 获取用于此 grain 激活的日记状态管理器 。
    /// </summary>
    protected IDurableStateManager StateManager { get; }

    /// <summary>
    /// 写下注册日记状态。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>A <see cref="ValueTask"/> 代表写作操作。</returns>
    protected ValueTask WriteStateAsync(CancellationToken cancellationToken = default) => StateManager.WriteStateAsync(cancellationToken);
}
