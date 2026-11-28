using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Streams
{
    /// <summary>
    /// 将 <see cref="StreamId"/> 映射到 <see cref="GrainId.Key"/> 的组件所实现的通用接口。
    /// </summary>
    public interface IStreamIdMapper
    {
        /// <summary>
        /// 获取 <see cref="GrainId.Key" />，它与指定的 <see cref="StreamId" /> 相对应。
        /// </summary>
        /// <param name="grainBindings">Grain 绑定。</param>
        /// <param name="streamId">流标识符。</param>
        /// <returns><see cref="GrainId.Key"/> 组成部分。</returns>
        IdSpan GetGrainKeyId(GrainBindings grainBindings, StreamId streamId);
    }
}
