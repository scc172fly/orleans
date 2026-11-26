namespace Orleans.Durable.Journaling;

/// <summary>
/// 创建日记国家管理器,用于独立识别的期刊,而无需启动 grain 。
/// </summary>
public interface IJournaledStateManagerFactory
{
    /// <summary>
    /// 为提供的日记 ID 创建独立日记国家管理器 。
    /// </summary>
    /// <param name="journalId">日记id。</param>
    /// <returns>记事的国家经理。</returns>
    /// <remarks>
    /// 呼叫者建造国家机器,并使用
    /// 启动管理器前<see cref="IJournaledStateManager.RegisterStateMachine"/> 。
    /// 国家机器依赖性及其寿命由打电话者提供。
    /// 每位管理人员拥有其日记处理和资源。
    /// 打电话者必须启动和不同步处置返回的经理人,包括
    /// 启动时从 grain 内创建它。
    /// </remarks>
    IJournaledStateManager CreateStandalone(JournalId journalId);
}
