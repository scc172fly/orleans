using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 将 <see cref="ChannelId"/> 映射到 <see cref="GrainId.Key"/> 的组件的通用接口。
    /// </summary>
    public interface IChannelIdMapper
    {
        /// <summary>
        /// 获取 <see cref="GrainId.Key" />，其映射自所提供的 <see cref="ChannelId" />。
        /// </summary>
        /// <param name="grainBindings">Grain 绑定。</param>
        /// <param name="streamId">流标识符。（译注：此参数实际为 ChannelId，即广播通道标识符。）</param>
        /// <returns><see cref="GrainId.Key"/> 组成部分。</returns>
        IdSpan GetGrainKeyId(GrainBindings grainBindings, ChannelId streamId);
    }
}
