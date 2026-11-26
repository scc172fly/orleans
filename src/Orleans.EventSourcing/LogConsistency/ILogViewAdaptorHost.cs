namespace Orleans.EventSourcing
{
    /// <summary>
    /// 由所有使用日志-视图一致性的 grains 用户所执行的界面
    /// 它使日志视图调整器访问 grain- 特定信息和回调。
    /// </summary>
    /// <typeparam name="TLogView">日志视图类型</typeparam>
    /// <typeparam name="TLogEntry">类日志条目类型</typeparam>
    public interface ILogViewAdaptorHost<TLogView, TLogEntry> : IConnectionIssueListener
    {
        /// <summary>
        /// 实施观点过渡。 
        /// 任何被抛出的任何例外将被抓住并登入记录作为警告。/ >
        /// </summary>
        void UpdateView(TLogView view, TLogEntry entry);

        /// <summary>
        /// 通知主机状态变化 grain 。 
        /// 在临时或确认的状态变化时,调用 <see cref="ILogViewAdaptor{TLogView,TLogEntry}"/> 。
        /// 执行在是否和多少批次变更通知方面可能有所不同。
        /// 任何被抛出的任何例外将被抓住并登入记录作为警告。/ >
        /// </summary>
        void OnViewChanged(bool tentative, bool confirmed);

    }


}
