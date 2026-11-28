
using Orleans.Storage;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 日志视图适配器工厂必须实现的接口。
    /// </summary>
    public interface ILogViewAdaptorFactory
    {
        /// <summary>如果构造适配器需要存储提供程序，则返回 true。</summary>
        bool UsesStorageProvider { get; }

        /// <summary>
        /// 构造一个 <see cref="ILogViewAdaptor{TLogView,TLogEntry}"/>，以安装到给定的宿主 Grain 中。
        /// </summary>
        /// <typeparam name="TLogView">视图的类型。</typeparam>
        /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
        /// <param name="hostGrain">承载此适配器的 Grain。</param>
        /// <param name="initialState">此视图的初始状态。</param>
        /// <param name="grainTypeName">Grain 的类型名称。</param>
        /// <param name="grainStorage">存储提供程序。</param>
        /// <param name="services">多集群一致性协议所需的运行时服务。</param>
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
