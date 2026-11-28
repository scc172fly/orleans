
using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 跟踪要注入的存储异常的 Grain。
    /// </summary>
    public interface IStorageFaultGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 添加一个存储异常，在引用的 Grain 从存储提供程序读取状态时抛出。
        /// </summary>
        /// <returns>任务。</returns>
        Task AddFaultOnRead(GrainId grainId, Exception exception);

        /// <summary>
        /// 添加一个存储异常，在引用的 Grain 向存储提供程序写入状态时抛出。
        /// </summary>
        /// <returns>任务。</returns>
        Task AddFaultOnWrite(GrainId grainId, Exception exception);

        /// <summary>
        /// 添加一个存储异常，在引用的 Grain 清除存储提供程序中的状态时抛出。
        /// </summary>
        /// <returns>任务。</returns>
        Task AddFaultOnClear(GrainId grainId, Exception exception);

        /// <summary>
        /// 如果已针对该 Grain 引用的读取操作添加存储异常，则抛出该异常。
        /// </summary>
        Task OnRead(GrainId grainId);

        /// <summary>
        /// 如果已针对该 Grain 引用的写入操作添加存储异常，则抛出该异常。
        /// </summary>
        Task OnWrite(GrainId grainId);

        /// <summary>
        /// 如果已针对该 Grain 引用的清除状态操作添加存储异常，则抛出该异常。
        /// </summary>
        Task OnClear(GrainId grainId);
    }
}
