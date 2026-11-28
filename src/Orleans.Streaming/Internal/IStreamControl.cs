using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 流控制接口，使流运行时可以对流执行管理操作，
    /// 而无须考虑此流使用的具体泛型类型。
    /// </summary>
    internal interface IStreamControl
    {
        /// <summary>
        /// 执行此流的清理操作。
        /// </summary>
        /// <returns>表示此流清理操作完成的异步任务。</returns>
        Task Cleanup(bool cleanupProducers, bool cleanupConsumers);
    }
}
