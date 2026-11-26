using System.Threading.Tasks;

namespace Orleans.Streams
{
    /// <summary>
    /// 流流控制界面,让流流运行时间用于在流上执行管理操作 
    /// 无需担心此流所使用的具体通用类型
    /// </summary>
    internal interface IStreamControl
    {
        /// <summary>
        /// 为此流执行清理功能 。
        /// </summary>
        /// <returns>完成此流清理作业的完工承诺。</returns>
        Task Cleanup(bool cleanupProducers, bool cleanupConsumers);
    }
}
