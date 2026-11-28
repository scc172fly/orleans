using Orleans.Runtime;

namespace Orleans.Streams.Filtering
{
    /// <summary>
    /// 提供筛选流的功能。
    /// </summary>
    public interface IStreamFilter
    {
        /// <summary>
        /// 返回一个值，指示是否应投递指定流数据项。
        /// </summary>
        /// <param name="streamId">流标识符。</param>
        /// <param name="item">流数据项。</param>
        /// <param name="filterData">筛选数据。</param>
        /// <returns>如果应投递流数据项，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        bool ShouldDeliver(StreamId streamId, object item, string? filterData);
    }

    internal sealed class NoOpStreamFilter : IStreamFilter
    {
        public bool ShouldDeliver(StreamId streamId, object item, string? filterData) => true;
    }
}
