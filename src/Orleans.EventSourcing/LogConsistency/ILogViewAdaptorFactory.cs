
using Orleans.Storage;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 用于日志视图调整器工厂的接口
    /// </summary>
    public interface ILogViewAdaptorFactory
    {
        /// <summary> 如果建造适应器需要存储提供者, 返回为真。 </summary>
        bool UsesStorageProvider { get; }

        /// <summary>
        /// 构造 a <see cref="ILogViewAdaptor{TLogView,TLogEntry}"/> 将安装在给定的主机中 grain.
        /// </summary>
        /// <typeparam name="TLogView">视图类型</typeparam>
        /// <typeparam name="TLogEntry">日志条目类型</typeparam>
        /// <param name="hostGrain">下 grain 正在托管此调整器的</param>
        /// <param name="initialState">此视图的初始状态</param>
        /// <param name="grainTypeName"> grain</param>的名称类型
        /// <param name="grainStorage">储存提供者</param>
        /// <param name="services">多部门多组协调协议的运行时间服务</param>
        ILogViewAdaptor<TLogView, TLogEntry> MakeLogViewAdaptor<TLogView, TLogEntry>(
            ILogViewAdaptorHost<TLogView, TLogEntry> hostGrain,
            TLogView initialState,
            string grainTypeName,
            IGrainStorage? grainStorage,
            ILogConsistencyProtocolServices services)

            where TLogView : class, new()
            where TLogEntry : class;

    }
}
