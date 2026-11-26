using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime
{
    /// <summary>
    /// 当地权威,每silo 获取关于其他 silos.
    /// </summary>
    public interface ISiloStatusOracle
    {
        /// <summary>
        /// 获取此 silo的当前状态。
        /// </summary>
        SiloStatus CurrentStatus { get; }

        /// <summary>
        /// 获取此 silo的名称。
        /// </summary>
        string SiloName { get; }

        /// <summary>
        /// 获取此 silo的地址。
        /// </summary>
        SiloAddress SiloAddress { get; }

        /// <summary>
        /// 获取当前活动的 silos。
        /// </summary>
        SiloAddress[] GetActiveSilos();

        /// <summary>
        /// 获取给定 silo状态。 
        /// 此方法返回对给定 silo状态的大致视图。 
        /// 特别是,这个神谕可能认为给定 silo 虽说它已经失败了,但还活着
        /// 如果这个神灵认为 silo 死了,它被权威地告诉 ISiloDirectory.
        /// </summary>
        /// <param name="siloAddress">A silo 我们关心的是他们的身份</param>
        /// <returns>特定国家的地位 silo.</returns>
        SiloStatus GetApproximateSiloStatus(SiloAddress siloAddress);

        /// <summary>
        /// 获取全部 silo的状态 。 
        /// 此方法返回所有 silo状态的近似视图。
        /// </summary>
        /// <param name="onlyActive">只包含当前被认为活动状态的 silo 。 如果假, 则包括全部。</param>
        /// <returns>列表列表 silo 状况。</returns>
        Dictionary<SiloAddress, SiloStatus> GetApproximateSiloStatuses(bool onlyActive = false);

        /// <summary>
        /// 获取 silo的名称。 
        /// Silo 名称假定为静态,且不因同一 silo的重新启动而改变。
        /// </summary>
        /// <param name="siloAddress">A silo 我们感兴趣的名字是谁</param>
        /// <param name="siloName">一个 silo 名称。</param>
        /// <returns>如果可以退回请求的姓名, 则TTrue, 否则是伪造的。</returns>
        bool TryGetSiloName(SiloAddress siloAddress, [NotNullWhen(true)] out string? siloName);

        /// <summary>
        /// 获得一个值, 显示当前 silo 是否对在目录上创建新激活有效或对目录查找有效。
        /// </summary>
        /// <returns>下 silo 问吧</returns>
        bool IsFunctionalDirectory(SiloAddress siloAddress);

        /// <summary>
        /// 获得一个值,显示当前 silo 是否已经死。
        /// </summary>
        /// <returns>下 silo 问吧</returns>
        bool IsDeadSilo(SiloAddress silo);

        /// <summary>
        /// 订阅所有 silos的状态事件 。 
        /// </summary>
        /// <param name="observer">要接收的同步界面会为观察员设置一个观察者 silo 状况通知。</param>
        /// <returns>表示订阅成功与否的值。</returns>
        bool SubscribeToSiloStatusEvents(ISiloStatusListener observer);

        /// <summary>
        /// UnSubscribe 全部状态事件 silos. 
        /// </summary>
        /// <returns>表示订阅成功与否的值。</returns>
        bool UnSubscribeFromSiloStatusEvents(ISiloStatusListener observer);
    }
}
