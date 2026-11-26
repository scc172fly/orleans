using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.BroadcastChannel
{
    /// <summary>
    /// 将 <see cref="ChannelId"/> 映射到 <see cref="GrainId.Key"/>的组件的共同界面
    /// </summary>
    public interface IChannelIdMapper
    {
        /// <summary>
        /// 获取提供给提供方的地图 <see cref="GrainId.Key" /> <see cref="ChannelId" />
        /// </summary>
        /// <param name="grainBindings">装订 grain 。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>下 <see cref="GrainId.Key"/> 构成部分。</returns>
        IdSpan GetGrainKeyId(GrainBindings grainBindings, ChannelId streamId);
    }
}
