namespace Orleans.EventSourcing
{
    /// <summary>
    /// 使用虚拟保护方法以对数一致 grains 实施的接口
    /// 用户可以取代这些功能,以便监测连接问题。
    /// </summary>
    public interface IConnectionIssueListener
    {
        /// <summary>
        /// 遇到某种连接问题时打电话来
        /// 调用的代码可以修改重试延迟, 如果需要的话, 以更改默认值 。
        /// </summary>
        void OnConnectionIssue(ConnectionIssue connectionIssue);

        /// <summary>
        /// 当先前报告的连接问题得到解决时,即被调用。
        /// </summary>
        void OnConnectionIssueResolved(ConnectionIssue connectionIssue);
    }

}
