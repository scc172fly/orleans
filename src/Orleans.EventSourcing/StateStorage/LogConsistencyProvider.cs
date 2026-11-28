using Orleans.Storage;

namespace Orleans.EventSourcing.StateStorage
{
    /// <summary>
    /// 使用任意标准存储提供程序将最新视图保存到主存储中的日志一致性提供程序。
    /// 支持多个集群连接到同一个主存储（通过 ETag 实现乐观并发控制）。
    ///<para>
    /// 日志本身是临时的，即实际不保存到存储中；主存储只保存最新视图（快照）和部分
    /// 元数据（日志位置及写入标志）。
    /// </para>
    /// </summary>
    public class LogConsistencyProvider : ILogViewAdaptorFactory
    {
        /// <inheritdoc/>
        public bool UsesStorageProvider
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc/>
        public ILogViewAdaptor<TView, TEntry> MakeLogViewAdaptor<TView, TEntry>(ILogViewAdaptorHost<TView, TEntry> hostGrain, TView initialState, string grainTypeName, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services)
            where TView : class, new()
            where TEntry : class
        {
            return new LogViewAdaptor<TView, TEntry>(hostGrain, initialState, grainStorage!, grainTypeName, services);
        }
    }
}
