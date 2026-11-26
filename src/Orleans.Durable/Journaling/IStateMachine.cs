namespace Orleans.Durable.Journaling;

/// <summary>
/// 定义日记支持的国家机器的重放、快照和确认协议。
/// </summary>
/// <remarks>
/// <para>
/// 执行由一个 <see cref="JournaledStateManager"/> 拥有,并存取
/// 一次从一个逻辑线条中选择一种逻辑线条。 他们参与以下日记生命周期:
/// </para>
/// <list type="bullet">
/// <item>
/// 用户代码同步同步转换模拟状态(通常通过援引编码助手来进行)
/// 并同时在当地应用突变, 并将相应的命令发送到日记上 。
/// </item>
/// <item>
/// 当申请要求写作时,国家经理会打电话
/// <see cref="PrepareWriteAsync"/> 建立国家持久性的先决条件,然后
/// <see cref="WritePendingEntries"/> (偶尔 <see cref="WriteSnapshot"/>)实现
/// 将日记冲到耐久存储处。
/// </item>
/// <item>
/// <see cref="OnWriteCompleted"/> 是在承认耐用书写时被援引的。
/// 需要知道国家何时实际持续执行(例如,
/// 以释放服务员或触发下游通知))应接上这一回召
/// 而不是将内部突变视为耐久的。
/// </item>
/// <item>
/// 写作失败或永久删除,使管理者永久地围住,并要求 grain 停止使用。
/// 在重放耐久条目之前,包括当启动程序重新尝试时,追偿呼叫 <see cref="Reset"/> 。
/// </item>
/// </list>
/// <para>
/// 应用代码为操作-当地数据中的可失传工作准备了可编篡的可编造工作,而只有阶段变化才涉及
/// 使用同一经理的跨部电话员共享已演化的突变。
/// 新的启动机制将产生一个新的经理和州级实例;
/// 重试初始化失败的重新设置, 重现现有实例 。
/// </para>
/// </remarks>
public interface IStateMachine
{
    /// <summary>
    /// 在日记恢复期间重放一个条目 。
    /// </summary>
    /// <param name="entry">要重放的条目。</param>
    /// <param name="context">重放上下文。</param>
    /// <remarks>
    /// 执行不得保留 <see cref="JournalEntry.Reader"/> 或提及该
    /// 此方法返回后备份存储, 除非复制数据 。
    /// </remarks>
    void ReplayEntry(JournalEntry entry, JournalReplayContext context);

    /// <summary>
    /// 重开州际关系
    /// </summary>
    /// <remarks>
    /// 如果国家有任何动荡状态,必须采用这种方法予以清除。
    /// 管理器在绑定流、 删除日记或退出状态时调用此方法 。
    /// </remarks>
    void Reset(JournalStreamWriter writer);

    /// <summary>
    /// 通知国家,所有先前的日记条目和快照都已应用。
    /// </summary>
    /// <remarks>
    /// 国家不应期望在采用这种方法后再有追回款项的条目,
    /// 除非 <see cref="Reset"/> 被调用将状态重置到初始状态。
    /// 这种方法将被调用到任何 <see cref="WritePendingEntries"/> 或 <see cref="WriteSnapshot"/> 调用之前。
    /// </remarks>
    void OnRecoveryCompleted() { }

    /// <summary>
    /// 确立了该国有待改变的持久性先决条件。
    /// </summary>
    /// <param name="cancellationToken">当日记所有者停止时取消准备的符号。</param>
    /// <returns>表示已准备好捕捉到此状态的完成。</returns>
    /// <remarks>
    /// 管理人员在捕捉每个附件或快照之前,先为每一个登记的国家做好准备,无论州是否
    /// 国家可以建立其他国家持有的变更的先决条件,或未写
    /// 不可进行修改。 准备是一无所有。 在非同步之后
    /// 完成准备, 管理员重复准备所有各州的准备, 以包含更改
    /// 预先准备的先决条件完全同步接听随后的电话
    /// 完全同步的通行器直接流入捕捉
    /// 完成准备工作,无需等待自己的日记经理再做一次业务;
    /// 因为该操作在目前写入后按序列排列。
    /// 故障报告 <see cref="JournaledStatePreCommitException"/> ,保留待更改
    /// 用于明确的写重试。
    /// </remarks>
    ValueTask PrepareWriteAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>
    /// 正在撰写日记的状态更改。
    /// </summary>
    /// <param name="writer">日记流写作器。</param>
    void WritePendingEntries(JournalStreamWriter writer);

    /// <summary>
    /// 向提供作者写下国家简况。
    /// </summary>
    /// <param name="writer">日记流写作器。</param>
    void WriteSnapshot(JournalStreamWriter writer);

    /// <summary>
    /// 通知国家,它所写的所有先前的日记条目和快照都已书面保存到稳定存放处。
    /// </summary>
    /// <remarks>
    /// 管理器为每写一个文件, 记录每个状态: 它调用 <see cref="WritePendingEntries"/> 或
    /// <see cref="WriteSnapshot"/> 每一个州,坚持结果,然后将这种方法称为每个州。
    /// 抓取后拍摄属于下个写入, 所以执行只承认它所记录的更改
    /// 写作此。 记录任何事物的文字, 没有任何存在, 也不打电话来。
    /// </remarks>
    void OnWriteCompleted() { }
}
