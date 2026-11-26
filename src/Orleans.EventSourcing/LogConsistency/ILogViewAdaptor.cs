using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 日志查看调整器是 <see cref="LogConsistentGrain{T}"/>的存储界面,其状态被定义为日志视图。 
    ///<para>
    /// 每一个适配器有一个 grain,由 <see cref="ILogViewAdaptorFactory"/> 时 grain 激活。
    ///</para>
    /// </summary>
    /// <typeparam name="TLogView"> 日志视图类型 </typeparam>
    /// <typeparam name="TLogEntry"> 日志条目类型 </typeparam>
    public interface ILogViewAdaptor<TLogView, TLogEntry> :
          ILogViewRead<TLogView, TLogEntry>,
          ILogViewUpdate<TLogEntry>,
          ILogConsistencyDiagnostics
        where TLogView : new()
    {
        /// <summary>激活期间在用户定义 <see cref="Grain.OnActivateAsync(CancellationToken)"/>之前的激活中调用。</summary>
        Task PreOnActivate();

        /// <summary>激活期间调用, 紧接自定义 <see cref="Grain.OnActivateAsync(CancellationToken)"/>之后。</summary>
        Task PostOnActivate();

        /// <summary>停止使用时,在用户定义的 <see cref="Grain.OnDeactivateAsync(DeactivationReason, CancellationToken)"/>之后立即调用。</summary>
        Task PostOnDeactivate();
    }

    internal interface ICancellationAwareLogViewAdaptor
    {
        Task PostOnDeactivate(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 读取日志视图的接口 。
    /// </summary>
    /// <typeparam name="TView">视图类型( grain).</typeparam>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    public interface ILogViewRead<TView, TLogEntry>
    {
        /// <summary>
        /// 本地对日志的暂时本地视图(反映经确认和未经证实的条目)
        /// </summary>
        TView TentativeView { get; }

        /// <summary>
        /// 确认的日志视图( 只反映已确认的条目)
        /// </summary>
        TView ConfirmedView { get; }

        /// <summary>
        /// 经确认的日志前缀长度
        /// </summary>
        int ConfirmedVersion { get; }

        /// <summary>
        /// 已确认的前缀中尚未出现已提交条目列表 。
        /// </summary>
        IEnumerable<TLogEntry> UnconfirmedSuffix { get; }

        /// <summary>
        /// 试图从存储处取回日志的一部分, 可能的话。 丢弃 <see cref="NotSupportedException"/> 如果
        /// 日志无法读取, 这取决于所使用的提供者以及如何配置它们。
        /// </summary>
        /// <param name="fromVersion">启动位置 </param>
        /// <param name="toVersion">终点</param>
        /// <returns>a </returns>
        Task<IReadOnlyList<TLogEntry>> RetrieveLogSegment(int fromVersion, int toVersion);

    }

    /// <summary>
    /// 更新日志的接口 。
    /// </summary>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    public interface ILogViewUpdate<TLogEntry>
    {
        /// <summary>
        /// 提交一个单日志条目,并附在全球日志上,
        /// 现任或后任职位。
        /// </summary>
        void Submit(TLogEntry entry);

        /// <summary>
        /// 提交一系列要原子附加到全球日志的日志条目,
        /// 现任或后任职位。
        /// </summary>
        void SubmitRange(IEnumerable<TLogEntry> entries);

        /// <summary>
        /// 尝试在日志当前位置上附加单个日志条目 。
        /// </summary>
        /// <returns>真实的,如果条目被成功补并或虚假 
        /// 如果存在同值货币冲突(i.e. 。 以前还附上了其他一些条目)。
        /// </returns>
        Task<bool> TryAppend(TLogEntry entry);

        /// <summary>
        /// 尝试在日志当前位置上从原子角度附加一系列日志条目 。
        /// </summary>
        /// <returns>真实的,如果条目被成功补入或虚假 
        /// 如果存在同值货币冲突(i.e. 。 以前还附上了其他一些条目)。
        /// </returns>
        Task<bool> TryAppendRange(IEnumerable<TLogEntry> entries);

        /// <summary>
        /// 确认所有提交的条目。
        ///<para>等待所有先前提交的条目出现在经确认的日志前缀中。</para>
        /// </summary>
        /// <returns>所有条目确认后完成的任务。</returns>
        Task ConfirmSubmittedEntries();

        /// <summary>
        /// 获取最新的日志视图并确认所有提交的条目 。
        ///<para>等待所有先前提交的条目出现在经确认的日志前缀中,并强制更新经确认的前缀。</para>
        /// </summary>
        /// <returns>一项任务,该任务在得到最新版本并确认所有条目后完成。</returns>
        Task Synchronize();

        /// <summary>
        /// 完全清除日志流。 投 <see cref="NotSupportedException"/> 如果
        /// 日志流不支持清除 。
        /// </summary>
        /// <param name="cancellationToken">取消操作的取消符号。</param>
        /// <returns>代表无同步清晰操作的任务。</returns>
        Task ClearLogAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException($"The operation {nameof(ClearLogAsync)} is not supported by this implementation of {nameof(ILogViewUpdate<TLogEntry>)}.");
        }
    }
}
