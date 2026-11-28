namespace Orleans.EventSourcing
{
    /// <summary>
    /// 日志一致性 Grain 通过受保护的虚方法实现的接口，
    /// 用户可重写这些方法以监控连接问题。
    /// </summary>
    public interface IConnectionIssueListener
    {
        /// <summary>
        /// 遇到某种连接问题时调用。
        /// 被调用的代码可以按需修改重试延迟，以改变默认值。
        /// </summary>
        void OnConnectionIssue(ConnectionIssue connectionIssue);

        /// <summary>
        /// 先前报告的连接问题已解决时调用。
        /// </summary>
        void OnConnectionIssueResolved(ConnectionIssue connectionIssue);
    }

}
