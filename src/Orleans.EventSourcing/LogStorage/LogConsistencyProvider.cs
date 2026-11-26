using Orleans.Storage;


namespace Orleans.EventSourcing.LogStorage
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

        /// <summary>
        /// Make 日志查看适配器 
        /// </summary>
        /// <typeparam name="TView">视图类型</typeparam>
        /// <typeparam name="TEntry">日志条目类型</typeparam>
        /// <param name="hostGrain">下 grain 正在托管此调整器的</param>
        /// <param name="initialState">此视图的初始状态</param>
        /// <param name="grainTypeName"> grain</param>的名称类型
        /// <param name="grainStorage">储存提供者</param>
        /// <param name="services">多部门多组协调协议的运行时间服务</param>
        public ILogViewAdaptor<TView, TEntry> MakeLogViewAdaptor<TView, TEntry>(ILogViewAdaptorHost<TView, TEntry> hostGrain, TView initialState, string grainTypeName, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services)
            where TView : class, new()
            where TEntry : class
        {
            return new LogViewAdaptor<TView, TEntry>(hostGrain, initialState, grainStorage!, grainTypeName, services);
        }
    }
}
