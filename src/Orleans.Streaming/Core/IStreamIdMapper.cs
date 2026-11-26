using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 将 <see cref="StreamId"/> 映射到 <see cref="GrainId.Key"/>的组件的共同界面
    /// </summary>
    public interface IStreamIdMapper
    {
        /// <summary>
        /// 得到 <see cref="GrainId.Key" /> 提供地图 <see cref="StreamId" />
        /// </summary>
        /// <param name="grainBindings">装订 grain 。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>下 <see cref="GrainId.Key"/> 构成部分。</returns>
        IdSpan GetGrainKeyId(GrainBindings grainBindings, StreamId streamId);
    }
}
