using System.Collections.Generic;

namespace Orleans.Runtime.Placement
{
    /// <summary>
    /// 描述一个安置目标,即 grain 以及触发 grain 安置的请求的背景。
    /// </summary>
    public readonly struct PlacementTarget
    {
        /// <summary>
        /// 初始化 <see cref="PlacementTarget"/> 结构的新实例。
        /// </summary>
        /// <param name="grainIdentity">下 grain 成为目标。</param>
        /// <param name="requestContextData">触发放置的请求的 <see cref="RequestContext"/> 字典。</param>
        /// <param name="interfaceType">请求的接口。</param>
        /// <param name="interfaceVersion">请求的接口版本。</param>
        public PlacementTarget(GrainId grainIdentity, Dictionary<string, object> requestContextData, GrainInterfaceType interfaceType, ushort interfaceVersion)
        {
            this.GrainIdentity = grainIdentity;
            this.InterfaceType = interfaceType;
            this.InterfaceVersion = interfaceVersion;
            this.RequestContextData = requestContextData;
        }

        /// <summary>
        /// 使 grain 成为目标。
        /// </summary>
        public GrainId GrainIdentity { get; }

        /// <summary>
        /// 获取触发此安插请求的 grain 所调用的接口的界面类型。
        /// </summary>
        public GrainInterfaceType InterfaceType { get; }

        /// <summary>
        /// 获取请求的接口版本 。
        /// </summary>
        public ushort InterfaceVersion { get; }

        /// <summary>
        /// 获取触发位置设置的请求的 <see cref="RequestContext"/> 字典。
        /// </summary>
        public Dictionary<string, object> RequestContextData { get; }
    }
}
