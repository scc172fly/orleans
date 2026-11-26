
using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 跟踪要注入的存储例外Grain 。
    /// </summary>
    public interface IStorageFaultGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 添加一个存储例外,当引用的 grain 读取储存提供者提供的状态时丢弃
        /// </summary>
        /// <returns>任务。</returns>
        Task AddFaultOnRead(GrainId grainId, Exception exception);

        /// <summary>
        /// 添加一个存储例外, 当引用 grain 向存储提供者写到状态时要丢弃的存储例外
        /// </summary>
        /// <returns>任务。</returns>
        Task AddFaultOnWrite(GrainId grainId, Exception exception);

        /// <summary>
        /// 添加一个存储例外,以便在引用的 grain 清除储存提供者中状态时丢弃
        /// </summary>
        /// <returns>任务。</returns>
        Task AddFaultOnClear(GrainId grainId, Exception exception);

        /// <summary>
        /// 如果在 grain 参考文献中添加了存储例外,则丢弃存储例外。
        /// </summary>
        Task OnRead(GrainId grainId);

        /// <summary>
        /// 如果为书面 grain 参考添加了存储例外,则丢弃此例外。
        /// </summary>
        Task OnWrite(GrainId grainId);

        /// <summary>
        /// 如果为清理状态的 grain 参考文献添加了存储例外,则丢弃此例外 。
        /// </summary>
        Task OnClear(GrainId grainId);
    }
}
