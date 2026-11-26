using System.Diagnostics.CodeAnalysis;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 管理 grain的耐久状态,其命名组件与日记共享并写入确认边界。
/// </summary>
/// <remarks>
/// 在启动前,在 grain 建造或同步启动装置期间宣布状态部件。
/// 使用激活自控操作执行操作操作时运行的操作。
/// 恢复完成后,即可启动和提供状态内容。
/// </remarks>
public interface IDurableStateManager
{
    /// <summary>
    /// 获得现有的州组成部分,或利用登记执行的申请合同建立这一构成部分。
    /// </summary>
    /// <typeparam name="TState">国家的申请合同</typeparam>
    /// <param name="name">稳定, 例常, 体察到案件敏感的国名 。</param>
    /// <returns>现有或新建的国家实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 名称有不兼容的状态类型,合同没有登记,或初始化已经开始,国家不存在。
    /// </exception>
    TState GetOrAddState<TState>(string name) where TState : class;

    /// <summary>
    /// 试图在不创建现有国家组成部分的情况下获取现有的州部分。
    /// </summary>
    /// <typeparam name="TState">国家的申请合同</typeparam>
    /// <param name="name">稳定, 例常, 体察到案件敏感的国名 。</param>
    /// <param name="state">找到时现有状态。</param>
    /// <returns><see langword="true"/> 如果存在兼容状态;否则, <see langword="false"/>。</returns>
    /// <exception cref="InvalidOperationException">名称有不兼容的状态类型。</exception>
    bool TryGetState<TState>(string name, [NotNullWhen(true)] out TState? state) where TState : class;

    /// <summary>
    /// 共享日记的更改前一直保留着 。
    /// </summary>
    /// <remarks>
    /// 只有在确定它们可以安全承诺之后才发生突变。
    /// 使用此管理器的所有间断调用者, 因此写作可以包含另一个调用者的更改 。
    /// 保存确认确定耐久性。
    /// 经理; 新的激活恢复了耐久状态。
    /// 取消停止了呼叫者的等待; 已经排队的写作继续到存储结果 。
    /// </remarks>
    /// <param name="cancellationToken">取消调用者等候的标记。</param>
    /// <returns>代表书面确认的任务。</returns>
    ValueTask WriteStateAsync(CancellationToken cancellationToken = default);
}
