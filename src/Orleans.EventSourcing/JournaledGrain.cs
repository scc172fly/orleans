using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.EventSourcing.Common;
using Orleans.Storage;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 使用标准事件溯源术语的日志一致性 Grain 基类。
    /// 所有操作均具备可重入安全性。
    /// </summary>
    /// <typeparam name="TGrainState">Grain 状态的类型，即事件日志的聚合视图。</typeparam>
    public abstract class JournaledGrain<TGrainState> : JournaledGrain<TGrainState, object>
        where TGrainState : class, new()
    {
        /// <summary>
        /// 初始化 <see cref="JournaledGrain{TGrainState}"/> 类的新实例。
        /// </summary>
        protected JournaledGrain() { }
    }

    /// <summary>
    /// 使用标准事件溯源术语的日志一致性 Grain 基类。
    /// 所有操作均具备可重入安全性。
    /// </summary>
    /// <typeparam name="TGrainState">Grain 状态的类型，即事件日志的聚合视图。</typeparam>
    /// <typeparam name="TEventBase">事件的公共基类。</typeparam>
    public abstract class JournaledGrain<TGrainState, TEventBase> :
        LogConsistentGrain<TGrainState>,
        ILogConsistencyProtocolParticipant,
        ILogViewAdaptorHost<TGrainState, TEventBase>
        where TGrainState : class, new()
        where TEventBase : class
    {
        /// <summary>
        /// 初始化 <see cref="JournaledGrain{TGrainState, TEventBase}"/> 类的新实例。
        /// </summary>
        protected JournaledGrain() { }

        /// <summary>
        /// 引发一个事件。
        /// </summary>
        /// <param name="event">要引发的事件。</param>
        protected virtual void RaiseEvent<TEvent>(TEvent @event)
            where TEvent : TEventBase
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            LogViewAdaptor.Submit(@event);
        }

        /// <summary>
        /// 以原子序列的形式引发多个事件。
        /// </summary>
        /// <param name="events">要引发的事件。</param>
        protected virtual void RaiseEvents<TEvent>(IEnumerable<TEvent> events)
            where TEvent : TEventBase
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            LogViewAdaptor.SubmitRange((IEnumerable<TEventBase>)events);
        }

        /// <summary>
        /// 有条件地引发一个事件。
        /// 仅在没有冲突（即在此期间没有引发其他事件）时成功。
        /// </summary>
        /// <param name="event">要引发的事件。</param>
        /// <returns><see langword="true"/> 表示成功，<see langword="false"/> 表示发生冲突。</returns>
        protected virtual Task<bool> RaiseConditionalEvent<TEvent>(TEvent @event)
            where TEvent : TEventBase
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            return LogViewAdaptor.TryAppend(@event);
        }

        /// <summary>
        /// 有条件地以原子序列的形式引发多个事件。
        /// 仅在没有冲突（即在此期间没有引发其他事件）时成功。
        /// </summary>
        /// <param name="events">要引发的事件。</param>
        /// <returns><see langword="true"/> 表示成功，<see langword="false"/> 表示发生冲突。</returns>
        protected virtual Task<bool> RaiseConditionalEvents<TEvent>(IEnumerable<TEvent> events)
            where TEvent : TEventBase
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            return LogViewAdaptor.TryAppendRange((IEnumerable<TEventBase>)events);
        }

        /// <summary>
        /// 获取当前已确认状态。
        /// 仅包含已确认事件。
        /// </summary>
        protected TGrainState State
        {
            get { return this.LogViewAdaptor.ConfirmedView; }
        }

        /// <summary>
        /// 获取当前已确认状态的版本。
        /// 等于已确认事件的总数。
        /// </summary>
        protected int Version
        {
            get { return this.LogViewAdaptor.ConfirmedVersion; }
        }

        /// <summary>
        /// 每当暂定状态可能因本地或远程事件而改变时调用。
        /// <para>重写此方法以响应状态变化。</para>
        /// </summary>
        protected virtual void OnTentativeStateChanged()
        {
        }

        /// <summary>
        /// 获取当前暂定状态。
        /// 同时包含已确认和未确认事件。
        /// </summary>
        protected TGrainState TentativeState
        {
            get { return this.LogViewAdaptor.TentativeView; }
        }

        /// <summary>
        /// 当已确认状态可能发生变化（即已确认版本号变大）后调用。
        /// <para>重写此方法以响应已确认状态的变化。</para>
        /// </summary>
        protected virtual void OnStateChanged()
        {
            // 希望响应状态变化的日志化 Grain 可重写此方法。
        }

        /// <summary>
        /// 等待先前引发的所有事件均得到确认。
        /// <para>引发一个或多个事件后等待此操作，以确保事件在继续执行前已持久化，或者即使此 Grain 存在多个实例也能保证强一致性（线性一致性）。</para>
        /// </summary>
        /// <returns>在事件全部得到确认后完成的任务。</returns>
        protected Task ConfirmEvents()
        {
            return LogViewAdaptor.ConfirmSubmittedEntries();
        }

        /// <summary>
        /// 立即获取最新状态，并确认先前引发的所有事件。
        /// 其实际效果是强制与全局状态同步。
        /// <para>读取状态前等待此操作，以确保即使此 Grain 存在多个实例也具备强一致性（线性一致性）。</para>
        /// </summary>
        /// <returns>日志刷新完成且所有事件均得到确认后完成的任务。</returns>
        protected Task RefreshNow()
        {
            return LogViewAdaptor.Synchronize();
        }

        /// <summary>
        /// 返回当前未确认事件的队列。
        /// </summary>
        public IEnumerable<TEventBase> UnconfirmedEvents
        {
            get { return LogViewAdaptor.UnconfirmedSuffix; }
        }

        /// <summary>
        /// 默认情况下，日志化 Grain 激活时会等待，直到已从存储加载
        /// 最新视图。子类可以重写此行为，
        /// 并在需要时跳过等待。
        /// </summary>
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            return LogViewAdaptor.Synchronize();
        }

        /// <summary>
        /// 获取已确认事件序列中的一段，可能需要从存储中读取。
        /// 如果事件不可读取，则抛出 <see cref="NotSupportedException"/>。
        /// 事件是否可读取以及可读取的时长，取决于使用的提供程序及其配置。
        /// </summary>
        /// <param name="fromVersion">事件序列的起始位置。</param>
        /// <param name="toVersion">事件序列的结束位置。</param>
        /// <returns>返回两个版本之间的事件序列的任务。</returns>
        protected Task<IReadOnlyList<TEventBase>> RetrieveConfirmedEvents(int fromVersion, int toVersion)
        {
            if (fromVersion < 0)
                throw new ArgumentException("invalid range", nameof(fromVersion));
            if (toVersion < fromVersion || toVersion > LogViewAdaptor.ConfirmedVersion)
                throw new ArgumentException("invalid range", nameof(toVersion));

            return LogViewAdaptor.RetrieveLogSegment(fromVersion, toVersion);
        }

        /// <summary>
        /// 清除所有已确认事件的日志，将状态重置为初始状态，并丢弃所有未确认事件。
        /// 如果无法清除日志，则抛出 <see cref="NotSupportedException"/>。
        /// </summary>
        protected Task ClearLogAsync(CancellationToken cancellationToken = default)
        {
            return LogViewAdaptor.ClearLogAsync(cancellationToken);
        }

        /// <summary>
        /// 底层持久化或复制协议遇到某种连接问题时调用。
        /// <para>重写此方法以监控日志一致性协议的运行状况和/或
        /// 自定义重试延迟。
        /// 抛出的任何异常均由 <see cref="ILogViewAdaptorFactory"/> 捕获并记录。</para>
        /// </summary>
        /// <returns>重试前等待的时长。</returns>
        protected virtual void OnConnectionIssue(ConnectionIssue issue)
        {
        }

        /// <summary>
        /// 先前报告的连接问题已解决时调用。
        /// <para>重写此方法以监控日志一致性协议的运行状况。
        /// 抛出的任何异常均由 <see cref="ILogViewAdaptorFactory"/> 捕获并记录。</para>
        /// </summary>
        protected virtual void OnConnectionIssueResolved(ConnectionIssue issue)
        {
        }

        /// <inheritdoc />
        protected void EnableStatsCollection()
        {
            LogViewAdaptor.EnableStatsCollection();
        }

        /// <inheritdoc />
        protected void DisableStatsCollection()
        {
            LogViewAdaptor.DisableStatsCollection();
        }

        /// <inheritdoc />
        protected LogConsistencyStatistics? GetStats()
        {
            return LogViewAdaptor.GetStats();
        }

        /// <summary>
        /// 定义如何将事件应用到状态。除非子类重写此方法，否则它会
        /// 在状态上调用动态的“Apply”函数，并将事件作为参数。
        /// 此方法抛出的所有异常均由日志视图提供程序捕获并记录。
        /// <para>重写此方法，以自定义给定事件导致状态转换的方式。</para>
        /// </summary>
        /// <param name="state">状态。</param>
        /// <param name="event">事件。</param>
        protected virtual void TransitionState(TGrainState state, TEventBase @event)
        {
            dynamic s = state;
            dynamic e = @event;
            s.Apply(e);
        }

        /// <summary>
        /// 获取由日志一致性提供程序安装的日志一致性协议适配器。
        /// </summary>
        internal ILogViewAdaptor<TGrainState, TEventBase> LogViewAdaptor { get; private set; } = null!;

        /// <summary>
        /// 在 Grain 构造完成后立即调用，以安装适配器。
        /// 日志一致性提供程序包含一个工厂方法，使用为此 Grain 选择的类型构造适配器。
        /// </summary>
        protected override void InstallAdaptor(ILogViewAdaptorFactory factory, object initialState, string graintypename, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services)
        {
            // 调用日志一致性提供程序构造适配器，并传入类型实参。
            LogViewAdaptor = factory.MakeLogViewAdaptor<TGrainState, TEventBase>(this, (TGrainState)initialState, graintypename, grainStorage, services);
            (LogViewAdaptor as IInitializableLogViewAdaptor)?.EnsureConfirmedViewInitialized();
        }

        /// <summary>
        /// 如果未指定日志一致性提供程序，则使用默认存储提供程序存储带版本的状态。
        /// </summary>
        protected override ILogViewAdaptorFactory DefaultAdaptorFactory
        {
            get
            {
                return new StateStorage.DefaultAdaptorFactory();
            }
        }

        /// <summary>
        /// 由适配器调用，以在追加条目时更新视图。
        /// </summary>
        /// <param name="view">日志视图。</param>
        /// <param name="entry">条目。</param>
        void ILogViewAdaptorHost<TGrainState, TEventBase>.UpdateView(TGrainState view, TEventBase entry)
        {
            TransitionState(view, entry);
        }

        /// <summary>
        /// 通知日志视图适配器已进入激活流程（在用户级 OnActivate 之前调用）。
        /// </summary>
        async Task ILogConsistencyProtocolParticipant.PreActivateProtocolParticipant()
        {
            await LogViewAdaptor.PreOnActivate();
        }

        /// <inheritdoc/>
        Task ILogConsistencyProtocolParticipant.PreActivateProtocolParticipant(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LogViewAdaptor.PreOnActivate();
        }

        /// <summary>
        /// 通知日志视图适配器已进入激活流程（在用户级 OnActivate 之后调用）。
        /// </summary>
        async Task ILogConsistencyProtocolParticipant.PostActivateProtocolParticipant()
        {
            await LogViewAdaptor.PostOnActivate();
        }

        /// <inheritdoc/>
        Task ILogConsistencyProtocolParticipant.PostActivateProtocolParticipant(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LogViewAdaptor.PostOnActivate();
        }

        /// <summary>
        /// 通知日志视图适配器进行停用处理。
        /// </summary>
        Task ILogConsistencyProtocolParticipant.DeactivateProtocolParticipant()
        {
            return LogViewAdaptor.PostOnDeactivate();
        }

        /// <inheritdoc/>
        Task ILogConsistencyProtocolParticipant.DeactivateProtocolParticipant(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LogViewAdaptor is ICancellationAwareLogViewAdaptor cancellationAware
                ? cancellationAware.PostOnDeactivate(cancellationToken)
                : LogViewAdaptor.PostOnDeactivate().WaitAsync(cancellationToken);
        }

        /// <summary>
        /// 状态变化时由适配器调用。
        /// </summary>
        void ILogViewAdaptorHost<TGrainState, TEventBase>.OnViewChanged(bool tentative, bool confirmed)
        {
            if (tentative)
                OnTentativeStateChanged();
            if (confirmed)
                OnStateChanged();
        }

        /// <summary>
        /// 发生连接问题时由适配器调用。
        /// </summary>
        void IConnectionIssueListener.OnConnectionIssue(ConnectionIssue connectionIssue)
        {
            OnConnectionIssue(connectionIssue);
        }

        /// <summary>
        /// 连接问题解决时由适配器调用。
        /// </summary>
        void IConnectionIssueListener.OnConnectionIssueResolved(ConnectionIssue connectionIssue)
        {
            OnConnectionIssueResolved(connectionIssue);
        }
    }

}
