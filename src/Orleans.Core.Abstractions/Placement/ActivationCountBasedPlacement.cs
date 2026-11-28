using System;

namespace Orleans.Runtime
{
    /// <summary>
    /// 一种基于每台服务器上近期活跃 Grain 的数量、尝试实现近似均衡负载的放置策略。
    /// </summary>
    /// <remarks>
    /// 此放置策略旨在依据近期繁忙的 Grain 数量，将新激活实例放置到负载最轻的服务器上。
    /// 此策略包含一种机制：所有服务器定期将其激活实例总数发布给其他所有服务器。
    /// 随后，放置决策器通过检查最近一次
    /// 报告的激活实例数量，并根据当前服务器上放置决策器近期创建的激活实例数量预测当前激活实例数，
    /// 选择预计激活实例数量最少的服务器。在进行预测时，决策器随机选择若干服务器，
    /// 以避免多台独立服务器同时使同一服务器过载。默认随机选择两台服务器，
    /// 但可以通过 <c>Orleans.Runtime.ActivationCountBasedPlacementOptions</c> 配置此数量。
    /// <br/>
    /// 此算法基于 Michael David Mitzenmacher 的论文 The Power of Two Choices in Randomized Load Balancing（随机负载均衡中的两次选择之力）<see href="https://www.eecs.harvard.edu/~michaelm/postscripts/mythesis.pdf"/>，
    /// NGINX 也将其用于分布式负载均衡，详见文章 NGINX and the "Power of Two Choices" Load-Balancing Algorithm <see href="https://www.f5.com/company/blog/nginx/nginx-power-of-two-choices-load-balancing-algorithm"/>。
    /// <br/>
    /// 通过向 Grain 添加 <see cref="Orleans.Placement.ActivationCountBasedPlacementAttribute"/> 特性来配置此放置策略。
    /// </remarks>
    [Serializable, GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class ActivationCountBasedPlacement : PlacementStrategy
    {
        /// <summary>
        /// 获取此类的单例实例。
        /// </summary>
        internal static ActivationCountBasedPlacement Singleton { get; } = new ActivationCountBasedPlacement();
    }
}
