using System.Threading;
using System.Threading.Tasks;
using Orleans.Metadata;

namespace Orleans.Streams
{
    /// <summary>
    /// 存储持久化流队列检查点。
    /// </summary>
    [DefaultGrainType("stream.checkpoint")]
    public interface IStreamCheckpointerGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 加载检查点。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>检查点。</returns>
        ValueTask<string> Load(CancellationToken cancellationToken);

        /// <summary>
        /// 如果持久化值与预期值匹配，则更新检查点。
        /// </summary>
        /// <param name="offset">偏移量。</param>
        /// <param name="expectedCheckpoint">预期的持久化检查点。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>尝试更新后的持久化检查点。</returns>
        ValueTask<string> Update(
            string offset,
            string expectedCheckpoint,
            CancellationToken cancellationToken);
    }

    [DefaultGrainType("stream.checkpoint.configured")]
    internal interface IConfiguredStreamCheckpointerGrain : IStreamCheckpointerGrain
    {
    }
}
