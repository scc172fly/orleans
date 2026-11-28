using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 日志视图适配器是 <see cref="LogConsistentGrain{T}"/> 的存储接口，该 Grain 的状态定义为日志视图。
    ///<para>
    /// 每个 Grain 有一个适配器，由 <see cref="ILogViewAdaptorFactory"/> 在 Grain 激活时安装。
    ///</para>
    /// </summary>
    /// <typeparam name="TLogView">日志视图的类型。</typeparam>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    public interface ILogViewAdaptor<TLogView, TLogEntry> :
          ILogViewRead<TLogView, TLogEntry>,
          ILogViewUpdate<TLogEntry>,
          ILogConsistencyDiagnostics
        where TLogView : new()
    {
        /// <summary>在激活过程中，紧接用户定义的 <see cref="Grain.OnActivateAsync(CancellationToken)"/> 之前调用。</summary>
        Task PreOnActivate();

        /// <summary>在激活过程中，紧接用户定义的 <see cref="Grain.OnActivateAsync(CancellationToken)"/> 之后调用。</summary>
        Task PostOnActivate();

        /// <summary>在停用过程中，紧接用户定义的 <see cref="Grain.OnDeactivateAsync(DeactivationReason, CancellationToken)"/> 之后调用。</summary>
        Task PostOnDeactivate();
    }

    internal interface ICancellationAwareLogViewAdaptor
    {
        Task PostOnDeactivate(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 用于读取日志视图的接口。
    /// </summary>
    /// <typeparam name="TView">视图（Grain 状态）的类型。</typeparam>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    public interface ILogViewRead<TView, TLogEntry>
    {
        /// <summary>
        /// 日志的本地暂定视图（反映已确认和未确认的条目）。
        /// </summary>
        TView TentativeView { get; }

        /// <summary>
        /// 日志的已确认视图（仅反映已确认的条目）。
        /// </summary>
        TView ConfirmedView { get; }

        /// <summary>
        /// 日志已确认前缀的长度。
        /// </summary>
        int ConfirmedVersion { get; }

        /// <summary>
        /// 已提交但尚未出现在已确认前缀中的条目列表。
        /// </summary>
        IEnumerable<TLogEntry> UnconfirmedSuffix { get; }

        /// <summary>
        /// 尝试获取日志中的一段，可能需要从存储读取。若日志不可读取，则抛出 <see cref="NotSupportedException"/>；
        /// 日志是否可读取取决于使用的提供程序及其配置。
        /// </summary>
        /// <param name="fromVersion">起始位置。</param>
        /// <param name="toVersion">结束位置。</param>
        /// <returns>a </returns>
        Task<IReadOnlyList<TLogEntry>> RetrieveLogSegment(int fromVersion, int toVersion);

    }

    /// <summary>
    /// 用于更新日志的接口。
    /// </summary>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    public interface ILogViewUpdate<TLogEntry>
    {
        /// <summary>
        /// 提交单个日志条目以追加到全局日志，
        /// 追加位置为当前位置或此后的任意位置。
        /// </summary>
        void Submit(TLogEntry entry);

        /// <summary>
        /// 提交一系列日志条目以原子方式追加到全局日志，
        /// 追加位置为当前位置或此后的任意位置。
        /// </summary>
        void SubmitRange(IEnumerable<TLogEntry> entries);

        /// <summary>
        /// 尝试在日志当前位置追加单个日志条目。
        /// </summary>
        /// <returns>如果成功追加条目，则为 true；否则为 false，
        /// 即存在并发冲突（其他条目已先行追加）。
        /// </returns>
        Task<bool> TryAppend(TLogEntry entry);

        /// <summary>
        /// 尝试在日志当前位置以原子方式追加一系列日志条目。
        /// </summary>
        /// <returns>如果成功追加这些条目，则为 true；否则为 false，
        /// 即存在并发冲突（其他条目已先行追加）。
        /// </returns>
        Task<bool> TryAppendRange(IEnumerable<TLogEntry> entries);

        /// <summary>
        /// 确认所有已提交条目。
        /// <para>等待先前提交的所有条目均出现在日志的已确认前缀中。</para>
        /// </summary>
        /// <returns>所有条目均得到确认后完成的任务。</returns>
        Task ConfirmSubmittedEntries();

        /// <summary>
        /// 获取最新日志视图，并确认所有已提交条目。
        /// <para>等待先前提交的所有条目均出现在日志的已确认前缀中，并强制刷新已确认前缀。</para>
        /// </summary>
        /// <returns>获取最新版本并确认所有条目后完成的任务。</returns>
        Task Synchronize();

        /// <summary>
        /// 完全清除日志流。抛出 <see cref="NotSupportedException"/> 的情况是
        /// 日志流不支持清除。
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步清除操作的任务。</returns>
        Task ClearLogAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException($"The operation {nameof(ClearLogAsync)} is not supported by this implementation of {nameof(ILogViewUpdate<TLogEntry>)}.");
        }
    }
}
