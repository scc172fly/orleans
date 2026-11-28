using System.Collections.Generic;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 描述放置目标，包括 Grain 以及触发 Grain 放置的请求的上下文。
    /// </summary>
    public readonly struct PlacementTarget
    {
        /// <summary>
        /// 初始化 <see cref="PlacementTarget"/> 结构的新实例。
        /// </summary>
        /// <param name="grainIdentity">作为目标的 Grain。</param>
        /// <param name="requestContextData">触发放置的请求的 <see cref="RequestContext"/> 字典。</param>
        /// <param name="interfaceType">所请求的接口。</param>
        /// <param name="interfaceVersion">所请求的接口版本。</param>
        public PlacementTarget(GrainId grainIdentity, Dictionary<string, object> requestContextData, GrainInterfaceType interfaceType, ushort interfaceVersion)
        {
            this.GrainIdentity = grainIdentity;
            this.InterfaceType = interfaceType;
            this.InterfaceVersion = interfaceVersion;
            this.RequestContextData = requestContextData;
        }

        /// <summary>
        /// 获取作为目标的 Grain。
        /// </summary>
        public GrainId GrainIdentity { get; }

        /// <summary>
        /// 获取触发此放置请求的 Grain 调用所使用的接口类型。
        /// </summary>
        public GrainInterfaceType InterfaceType { get; }

        /// <summary>
        /// 获取所请求的接口版本。
        /// </summary>
        public ushort InterfaceVersion { get; }

        /// <summary>
        /// 获取触发放置的请求的 <see cref="RequestContext"/> 字典。
        /// </summary>
        public Dictionary<string, object> RequestContextData { get; }
    }
}
