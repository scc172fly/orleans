using System.Threading;
using System.Threading.Tasks;
using Orleans.Metadata;

namespace Orleans.Streams
{
    /// <summary>
    /// 储存一个长串排队检查站。
    /// </summary>
    [DefaultGrainType("stream.checkpoint")]
    public interface IStreamCheckpointerGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 装满了检查站
        /// </summary>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>检查站。</returns>
        ValueTask<string> Load(CancellationToken cancellationToken);

        /// <summary>
        /// 如果持续值与预期值相符,则更新检查站。
        /// </summary>
        /// <param name="offset">转自</param>
        /// <param name="expectedCheckpoint">预期的检查站将长期存在。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <returns>更新工作结束后,检查站一直维持。</returns>
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
