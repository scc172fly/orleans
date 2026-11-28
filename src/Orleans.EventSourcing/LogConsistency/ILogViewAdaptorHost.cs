namespace Orleans.EventSourcing
{
    /// <summary>
    /// 所有使用日志视图一致性的 Grain 实现的接口。
    /// 此接口使日志视图适配器能够访问特定于 Grain 的信息及回调。
    /// </summary>
    /// <typeparam name="TLogView">日志视图的类型。</typeparam>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    public interface ILogViewAdaptorHost<TLogView, TLogEntry> : IConnectionIssueListener
    {
        /// <summary>
        /// 视图转换的实现。
        /// 抛出的任何异常都会被捕获，并作为警告记录。
        /// </summary>
        void UpdateView(TLogView view, TLogEntry entry);

        /// <summary>
        /// 将状态变化通知宿主 Grain。
        /// 每当暂定状态或已确认状态变化时，由 <see cref="ILogViewAdaptor{TLogView,TLogEntry}"/> 调用。
        /// 不同实现可以选择是否批处理变化通知以及批处理的程度。
        /// 抛出的任何异常都会被捕获，并作为警告记录。
        /// </summary>
        void OnViewChanged(bool tentative, bool confirmed);

    }


}
