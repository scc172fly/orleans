using Orleans.Storage;


namespace Orleans.EventSourcing.LogStorage
{
    /// <summary>
    /// 使用任意标准存储提供程序将最新视图保存到主存储中的日志一致性提供程序。
    /// 支持多个集群连接到同一个主存储（通过 ETag 实现乐观并发控制）。
    ///<para>
    /// 日志本身是临时的，即实际不保存到存储中；主存储只保存最新视图（快照）和部分
    /// 元数据（日志位置及写入标志）。（译注：当前 LogStorage 实现将包含完整日志的 GlobalLog 写入存储；上述“仅保存最新视图”的描述与实现不符。）
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
        /// 创建日志视图适配器。
        /// </summary>
        /// <typeparam name="TView">视图的类型。</typeparam>
        /// <typeparam name="TEntry">日志条目的类型。</typeparam>
        /// <param name="hostGrain">承载此适配器的 Grain。</param>
        /// <param name="initialState">此视图的初始状态。</param>
        /// <param name="grainTypeName">Grain 的类型名称。</param>
        /// <param name="grainStorage">存储提供程序。</param>
        /// <param name="services">多集群一致性协议所需的运行时服务。</param>
        public ILogViewAdaptor<TView, TEntry> MakeLogViewAdaptor<TView, TEntry>(ILogViewAdaptorHost<TView, TEntry> hostGrain, TView initialState, string grainTypeName, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services)
            where TView : class, new()
            where TEntry : class
        {
            return new LogViewAdaptor<TView, TEntry>(hostGrain, initialState, grainStorage!, grainTypeName, services);
        }
    }
}
