using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime
{
    /// <summary>
    /// 各 Silo 本地关于其他 Silo 状态信息的权威来源。
    /// </summary>
    public interface ISiloStatusOracle
    {
        /// <summary>
        /// 获取此 Silo 的当前状态。
        /// </summary>
        SiloStatus CurrentStatus { get; }

        /// <summary>
        /// 获取此 Silo 的名称。
        /// </summary>
        string SiloName { get; }

        /// <summary>
        /// 获取此 Silo 的地址。
        /// </summary>
        SiloAddress SiloAddress { get; }

        /// <summary>
        /// 获取当前活跃的 Silo。
        /// </summary>
        SiloAddress[] GetActiveSilos();

        /// <summary>
        /// 获取给定 Silo 的状态。
        /// 此方法返回给定 Silo 状态的近似视图。
        /// 特别是，该状态信息源可能认为给定 Silo 仍存活，但它可能已经失效。
        /// 如果此状态信息源认为给定 Silo 已死亡，则该信息已由 ISiloDirectory 权威告知。
        /// </summary>
        /// <param name="siloAddress">需要查询其状态的 Silo。</param>
        /// <returns>给定 Silo 的状态。</returns>
        SiloStatus GetApproximateSiloStatus(SiloAddress siloAddress);

        /// <summary>
        /// 获取所有 Silo 的状态。
        /// 此方法返回所有 Silo 状态的近似视图。
        /// </summary>
        /// <param name="onlyActive">仅包含当前被视为活跃的 Silo；如果为 false，则包含所有 Silo。</param>
        /// <returns>Silo 状态列表。</returns>
        Dictionary<SiloAddress, SiloStatus> GetApproximateSiloStatuses(bool onlyActive = false);

        /// <summary>
        /// 获取 Silo 的名称。
        /// 假定 Silo 名称是静态的，同一 Silo 重启后不会改变。
        /// </summary>
        /// <param name="siloAddress">需要查询其名称的 Silo。</param>
        /// <param name="siloName">Silo 名称。</param>
        /// <returns>能够返回所请求名称时为 true，否则为 false。</returns>
        bool TryGetSiloName(SiloAddress siloAddress, [NotNullWhen(true)] out string? siloName);

        /// <summary>
        /// 获取一个值，指示当前 Silo 是否可用于创建新激活实例或执行目录查找。
        /// </summary>
        /// <returns>要查询的 Silo。</returns>
        bool IsFunctionalDirectory(SiloAddress siloAddress);

        /// <summary>
        /// 获取一个值，指示当前 Silo 是否已死亡。
        /// </summary>
        /// <returns>要查询的 Silo。</returns>
        bool IsDeadSilo(SiloAddress silo);

        /// <summary>
        /// 订阅所有 Silo 的状态事件。
        /// </summary>
        /// <param name="observer">接收 Silo 状态通知的异步观察者接口。</param>
        /// <returns>指示订阅是否成功的值。</returns>
        bool SubscribeToSiloStatusEvents(ISiloStatusListener observer);

        /// <summary>
        /// 取消订阅所有 Silo 的状态事件。
        /// </summary>
        /// <returns>指示订阅是否成功的值。</returns>
        bool UnSubscribeFromSiloStatusEvents(ISiloStatusListener observer);
    }
}
