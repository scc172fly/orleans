namespace Orleans.Durable.Journaling;

/// <summary>
/// 定义基于日志的状态机的重放、快照及确认协议。
/// </summary>
/// <remarks>
/// <para>
/// 实现由单个 <see cref="JournaledStateManager"/> 所有，并且
/// 每次仅从一个逻辑线程访问。它们按如下方式参与日志持久化生命周期：
/// </para>
/// <list type="bullet">
/// <item>
/// 用户代码同步修改内存状态（通常通过调用编解码器辅助方法，
/// 在本地应用变更并将相应命令发出到日志）。
/// </item>
/// <item>
/// 应用请求写入时，日志状态管理器调用
/// <see cref="PrepareWriteAsync"/> 建立状态持久化的前置条件，随后调用
/// <see cref="WritePendingEntries"/>（有时调用 <see cref="WriteSnapshot"/>）以实体化
/// 待提交变更，然后将日志刷新到持久化存储。
/// </item>
/// <item>
/// 持久化写入得到确认后调用 <see cref="OnWriteCompleted"/>。
/// 需要知道状态实际何时持久化的实现（例如为了
/// 释放等待者或触发下游通知），应使用此回调，
/// 而不是将内存变更视为已持久化。
/// </item>
/// <item>
/// 写入或删除失败会永久隔离管理器，并请求 Grain 停用。
/// 恢复在重放持久化条目之前调用 <see cref="Reset"/>，包括重试初始化的情况。
/// </item>
/// </list>
/// <para>
/// 应用程序代码在操作局部数据中准备可能失败的工作，并仅暂存
/// 可安全提交的变更。使用同一管理器的所有交错执行的调用方共享已暂存的变更。
/// 存储确认建立持久性。新的激活会创建新的管理器及状态实例；
/// 重试失败的初始化会重置并重放已有实例。
/// </para>
/// </remarks>
public interface IStateMachine
{
    /// <summary>
    /// 在日志恢复期间重放一条条目。
    /// </summary>
    /// <param name="entry">要重放的条目。</param>
    /// <param name="context">重放上下文。</param>
    /// <remarks>
    /// 此方法返回后，实现不得保留 <see cref="JournalEntry.Reader"/> 或对其
    /// 底层存储的引用，除非复制数据。
    /// </remarks>
    void ReplayEntry(JournalEntry entry, JournalReplayContext context);

    /// <summary>
    /// 重置状态。
    /// </summary>
    /// <remarks>
    /// 如果状态包含任何易失性状态，此方法必须将其清除。
    /// 管理器在绑定流、删除日志或使状态退役时调用此方法。
    /// </remarks>
    void Reset(JournalStreamWriter writer);

    /// <summary>
    /// 通知状态所有之前的日志条目及快照都已应用。
    /// </summary>
    /// <remarks>
    /// 调用此方法后，状态不应预期有额外恢复条目，
    /// 除非调用 <see cref="Reset"/> 将状态重置为初始状态。
    /// 此方法会在任何 <see cref="WritePendingEntries"/> 或 <see cref="WriteSnapshot"/> 调用之前执行。
    /// </remarks>
    void OnRecoveryCompleted() { }

    /// <summary>
    /// 建立此状态待提交变更的持久化前置条件。
    /// </summary>
    /// <param name="cancellationToken">日志所有者停止时取消准备操作的令牌。</param>
    /// <returns>表示此状态已可供捕获的完成操作。</returns>
    /// <remarks>
    /// 管理器在捕获每次追加或快照之前准备每个已注册状态，无论该状态是否
    /// 包含待提交变更，以便状态为其他状态持有的变更建立前置条件，或使包含
    /// 不得提交的待提交变更的写入失败。准备操作具有幂等性。异步
    /// 准备完成后，管理器重新准备所有状态，以包含交错执行轮次中
    /// 暂存的变更。已准备的前置条件在后续调用中同步完成，
    /// 并一直有效直至捕获完成。完整的同步准备轮次直接进入捕获过程。
    /// 准备过程不应等待自身日志管理器上的另一项操作，
    /// 因为该操作串行排在当前写入之后。
    /// 失败会报告 <see cref="JournaledStatePreCommitException"/> 并保留待提交变更，
    /// 以便显式重试写入。
    /// </remarks>
    ValueTask PrepareWriteAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>
    /// 将待提交的状态变更写入日志。
    /// </summary>
    /// <param name="writer">日志流写入器。</param>
    void WritePendingEntries(JournalStreamWriter writer);

    /// <summary>
    /// 将状态快照写入提供的写入器。
    /// </summary>
    /// <param name="writer">日志流写入器。</param>
    void WriteSnapshot(JournalStreamWriter writer);

    /// <summary>
    /// 通知状态它此前写入的所有日志条目及快照都已写入稳定存储。
    /// </summary>
    /// <remarks>
    /// 管理器为每次写入捕获每个状态：对每个状态调用 <see cref="WritePendingEntries"/> 或
    /// <see cref="WriteSnapshot"/>，持久化结果，随后对每个状态调用此方法。在该捕获之后
    /// 进行的变更属于下一次写入，因此实现只确认本次写入捕获的
    /// 变更。没有捕获任何内容的写入不持久化任何内容，也不调用此方法。
    /// </remarks>
    void OnWriteCompleted() { }
}
