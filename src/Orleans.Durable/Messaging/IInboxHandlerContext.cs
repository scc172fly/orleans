namespace Orleans.Durable.Messaging;

/// <summary>
/// 展示了收到的信息及其尝试范围终端结果。
/// </summary>
public interface IInboxHandlerContext
{
    /// <summary>
    /// 获取接收到的信息及其不可变的应用程序信头和有效载荷。
    /// </summary>
    /// <remarks>
    /// 上下文通过实际处理器完成,包括
    /// 完成后或失败后删除待定信件。在处理命令时,保持有效载荷和信头不变。
    /// </remarks>
    InboxMessage Message { get; }

    /// <summary>
    /// 同步阶段的箱完成和运输脱转,以进行积极尝试。
    /// </summary>
    /// <remarks>
    /// 先进行可失和非同步的工作,然后应用企业变异,并先分阶段发送信息
    /// 完全在同步最后街区中拨打。 从操作器返回, 不再等待 。
    /// 最终没有结果的结束 失败了激活, 它抛弃了它分阶段的变化, 信息再次运行
    /// 在收到日记后。
    /// 运行时拥有随后的日记书写作和确认。
    /// 在同一积极尝试的联结中重复完成。
    /// 当取消在最后块开始后到达时, 逻辑结果将自动取消 。
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">
    /// 上下文已退休或属于另一起激活或处理者企图。
    /// </exception>
    void Complete();

    /// <summary>
    /// 同步处理永久处理失败,将信息保留为一纸空文
    /// 记录其身份,以便运输脱转。
    /// </summary>
    /// <param name="reason">造成永久性故障的原因 是不空诊断</param>
    /// <remarks>
    /// 启动之前要安全地进行业务变更和发送信息,
    /// 时间流逝, 仍然无法完成 并一起完成
    /// 并拥有日记确认。 死亡信件记录了当前尝试数。
    /// 重复通话,原因相同,因为双方联手; 完整保存了已经没有用的一纸空文。
    /// 集结后取消合同保留了最终结果
    /// 使用申请回复和完整软件来完成正常业务拒绝结果。
    /// </remarks>
    /// <exception cref="System.ArgumentException">原因为空白。</exception>
    /// <exception cref="System.ArgumentNullException">理由无效。</exception>
    /// <exception cref="System.InvalidOperationException">
    /// 上下文已退休或属于另一激活或企图,或属于另一个
    /// 终端结果已经进入阶段。
    /// </exception>
    void Fail(string reason);
}
