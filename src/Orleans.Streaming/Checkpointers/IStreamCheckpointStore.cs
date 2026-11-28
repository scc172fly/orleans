using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams;

/// <summary>
/// 使用条件更新存储持久化流检查点。
/// </summary>
public interface IStreamCheckpointStore
{
    /// <summary>
    /// 加载当前检查点及其版本。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前检查点状态。</returns>
    ValueTask<StreamCheckpointStoreState> Load(CancellationToken cancellationToken);

    /// <summary>
    /// 如果 <paramref name="expectedVersion"/> 与持久化版本匹配，则更新检查点。
    /// </summary>
    /// <param name="checkpoint">要持久化的检查点。</param>
    /// <param name="expectedVersion">预期的持久化版本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 尝试更新后的持久化状态。如果预期版本不匹配，
    /// 返回的状态包含发生冲突的持久化检查点及其版本。
    /// </returns>
    ValueTask<StreamCheckpointStoreState> Update(
        string checkpoint,
        string expectedVersion,
        CancellationToken cancellationToken);
}
