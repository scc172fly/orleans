using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Streams;

/// <summary>
/// 利用有条件更新,储存一个持久性流检查站。
/// </summary>
public interface IStreamCheckpointStore
{
    /// <summary>
    /// 装入当前检查点及其版本。
    /// </summary>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>目前的检查站状态。</returns>
    ValueTask<StreamCheckpointStoreState> Load(CancellationToken cancellationToken);

    /// <summary>
    /// 如果 <paramref name="expectedVersion"/> 符合持续版本,则更新检查站。
    /// </summary>
    /// <param name="checkpoint">检查站将继续存在。</param>
    /// <param name="expectedVersion">预期的已存在版本。</param>
    /// <param name="cancellationToken">取消牌。</param>
    /// <returns>
    /// 更新尝试后的持续状态。 如果预期版本不匹配,
    /// 返回的州内有冲突持续的检查站和版本。
    /// </returns>
    ValueTask<StreamCheckpointStoreState> Update(
        string checkpoint,
        string expectedVersion,
        CancellationToken cancellationToken);
}
