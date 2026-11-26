using Orleans.Storage;

namespace Orleans.EventSourcing.StateStorage
{
    /// <summary>
    /// 使用任何标准的储存提供者,将最新视图存放在初级储存中,并保持对账的一致性。
    /// 支持连接同一主存储(通过电子标签进行乐观的同价货币控制)的多个集群
    ///<para>
    /// 日志本身是短暂的, i.e。 实际上没有保存到存储中 - 只有最新的视图( snapshot) 和一些 
    /// 元数据(日志位置和写旗)存储在主数据中。 
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
