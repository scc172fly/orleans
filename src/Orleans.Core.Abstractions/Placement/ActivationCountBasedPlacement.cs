using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 一种定位策略,试图根据每个服务器最近启用的 grains 数量实现大约甚至负载。
    /// </summary>
    /// <remarks>
    /// 安置战略的目的是建立新的 grain 根据最近忙碌次数在最不重装入服务器上的激活 grains.
    /// 其中包括一个机制,所有服务器定期将其全部激活数公布于所有其他服务器。
    /// 安置主任然后选择服务器,通过检查最近的服务器来预测其启动率最少
    /// 依据最近由
    /// 在当前服务器上设置主任。 导演在做出此预测时随机选择一些服务器,
    /// 试图避免多个独立的服务器在同一个服务器上超载。 默认情况下, 随机选择两个服务器,
    /// 但此值可以通过 <c>Orleans.Runtime.ActivationCountBasedPlacementOptions</c>配置。
    /// <br/>
    /// 以麦克·戴维·米津马赫 <see href="https://www.eecs.harvard.edu/~michaelm/postscripts/mythesis.pdf"/>的论文《随机载重平衡中两种选择的力量》为基础,
    /// 并用于NGINX, 如NGINX和“双选权” <see href="https://www.f5.com/company/blog/nginx/nginx-power-of-two-choices-load-balancing-algorithm"/>中所述的分布式负载平衡。
    /// <br/>
    /// 此配置策略的配置方法是将 <see cref="Orleans.Placement.ActivationCountBasedPlacementAttribute"/> 属性添加到 grain。
    /// </remarks>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class ActivationCountBasedPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获得单吨的例子 这个类。
        /// </summary>
        internal static ActivationCountBasedPlacement Singleton { get; } = new ActivationCountBasedPlacement();
    }
}
