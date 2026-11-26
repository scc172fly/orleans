using Orleans.Runtime;

namespace Orleans.Streams.Filtering
{
    /// <summary>
    /// 过滤流的功能。
    /// </summary>
    public interface IStreamFilter
    {
        /// <summary>
        /// 返回一个值,以显示是否应该交付指定的流项。
        /// </summary>
        /// <param name="streamId">流体识别符号。</param>
        /// <param name="item">流项目。</param>
        /// <param name="filterData">过滤数据。</param>
        /// <returns><see langword="true" /> 如果流项目应交付,则以其他方式 <see langword="false" /> 。</returns>
        bool ShouldDeliver(StreamId streamId, object item, string? filterData);
    }

    internal sealed class NoOpStreamFilter : IStreamFilter
    {
        public bool ShouldDeliver(StreamId streamId, object item, string? filterData) => true;
    }
}
