using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans.EventSourcing.Common;
using Orleans.Storage;

namespace Orleans.EventSourcing
{
    /// <summary>
    /// 使用标准事件承包术语的日志一致 grains 基准类别。
    /// 所有操作都是再生安全
    /// </summary>
    /// <typeparam name="TGrainState"> grain 状态的类型, i.e。事件日志的总视图。</typeparam>
    public abstract class JournaledGrain<TGrainState> : JournaledGrain<TGrainState, object>
        where TGrainState : class, new()
    {
        /// <summary>
        /// 初始化 <see cref="JournaledGrain{TGrainState}"/> 类的新实例。
        /// </summary>
        protected JournaledGrain() { }
    }

    /// <summary>
    /// 使用标准事件承包术语的日志一致 grains 基准类别。
    /// 所有操作都是再生安全
    /// </summary>
    /// <typeparam name="TGrainState"> grain 状态的类型, i.e。事件日志的总视图。</typeparam>
    /// <typeparam name="TEventBase">事件的共同基数</typeparam>
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
        /// 升起一个事件。
        /// </summary>
        /// <param name="event">活动要提出。</param>
        protected virtual void RaiseEvent<TEvent>(TEvent @event)
            where TEvent : TEventBase
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            LogViewAdaptor.Submit(@event);
        }

        /// <summary>
        /// 以原子序列的形式 引发多重事件
        /// </summary>
        /// <param name="events">需要提出事件。</param>
        protected virtual void RaiseEvents<TEvent>(IEnumerable<TEvent> events)
            where TEvent : TEventBase
        {
            if (events == null) throw new ArgumentNullException(nameof(events));

            LogViewAdaptor.SubmitRange((IEnumerable<TEventBase>)events);
        }

        /// <summary>
        /// 有条件地提出事件。 
        /// 只有在没有冲突的情况下,即在此期间没有提出其他事件时,才能得奖。
        /// </summary>
        /// <param name="event">活动要提出。</param>
        /// <returns><see langword="true"/> 如果成功的话 <see langword="false"/> 如果有冲突的话</returns>
        protected virtual Task<bool> RaiseConditionalEvent<TEvent>(TEvent @event)
            where TEvent : TEventBase
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            return LogViewAdaptor.TryAppend(@event);
        }

        /// <summary>
        /// 以原子序列为条件 提出多个事件 
        /// 只有在没有冲突的情况下,即在此期间没有提出其他事件时,才能得奖。
        /// </summary>
        /// <param name="events">提出</param>的事件
        /// <returns><see langword="true"/> 如果成功的话 <see langword="false"/> 如果有冲突的话</returns>
        protected virtual Task<bool> RaiseConditionalEvents<TEvent>(IEnumerable<TEvent> events)
            where TEvent : TEventBase
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            return LogViewAdaptor.TryAppendRange((IEnumerable<TEventBase>)events);
        }

        /// <summary>
        /// 获得当前确认状态。 
        /// 仅包括已确认的事件。
        /// </summary>
        protected TGrainState State
        {
            get { return this.LogViewAdaptor.ConfirmedView; }
        }

        /// <summary>
        /// 获取当前确认状态的版本 。 
        /// 等于经证实的事件总数。
        /// </summary>
        protected int Version
        {
            get { return this.LogViewAdaptor.ConfirmedVersion; }
        }

        /// <summary>
        /// 每当临时状态因局部或远程事件而发生变化时,都打电话。
        /// <para>覆盖此选项以对状态变化作出反应。</para>
        /// </summary>
        protected virtual void OnTentativeStateChanged()
        {
        }

        /// <summary>
        /// 获取当前暂定状态 。
        /// 包括经证实和未经证实的事件。
        /// </summary>
        protected TGrainState TentativeState
        {
            get { return this.LogViewAdaptor.TentativeView; }
        }

        /// <summary>
        /// 调用时,经确认的状态可能已经改变(i.e.经确认的版本号更大)。
        /// <para>覆盖此选项以对确认状态的更改作出反应。</para>
        /// </summary>
        protected virtual void OnStateChanged()
        {
            // 被想要对状态变化作出反应的日记 grains 所覆盖
        }

        /// <summary>
        /// 等到所有先前提到的事件都得到确认。 
        /// <para>提出一个或多个事件后,等待这一结果,以确保事发之前活动持续不已,或保证强有力的一致性(线性),即使有多个此类情况 grain</para>
        /// </summary>
        /// <returns>任务一旦确认事件后完成</returns>
        protected Task ConfirmEvents()
        {
            return LogViewAdaptor.ConfirmSubmittedEntries();
        }

        /// <summary>
        /// 并证实所有先前提到的事件。 
        /// 有效地,这强制要求与全球同步。
        /// <para>等待这一结果,然后再阅读国家,以确保强有力的一致性(线性),即使存在多种情况 grain</para>
        /// </summary>
        /// <returns>一项任务,一旦日志刷新且事件得到确认后完成。</returns>
        protected Task RefreshNow()
        {
            return LogViewAdaptor.Synchronize();
        }

        /// <summary>
        /// 返回当前未证实事件的队列。
        /// </summary>
        public IEnumerable<TEventBase> UnconfirmedEvents
        {
            get { return LogViewAdaptor.UnconfirmedSuffix; }
        }

        /// <summary>
        /// 默认情况下, 激活时日记 grain 将等待它装入最新
        /// 子类可以推翻这种行为
        /// 并且如果愿意,就跳过等待。
        /// </summary>
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            return LogViewAdaptor.Synchronize();
        }

        /// <summary>
        /// 获取确认的事件序列的一部分,可能从存储处获得。 
        /// 如果事件无法读取, 则丢弃 <see cref="NotSupportedException"/> 。
        /// 事件是否可用,时间长短如何,取决于使用什么提供者以及如何配置。
        /// </summary>
        /// <param name="fromVersion">启动事件序列的位置</param>
        /// <param name="toVersion">结束的事件序列的位置</param>
        /// <returns>任务, 返回两个版本之间事件序列的序列</returns>
        protected Task<IReadOnlyList<TEventBase>> RetrieveConfirmedEvents(int fromVersion, int toVersion)
        {
            if (fromVersion < 0)
                throw new ArgumentException("invalid range", nameof(fromVersion));
            if (toVersion < fromVersion || toVersion > LogViewAdaptor.ConfirmedVersion)
                throw new ArgumentException("invalid range", nameof(toVersion));

            return LogViewAdaptor.RetrieveLogSegment(fromVersion, toVersion);
        }

        /// <summary>
        /// 清除所有已确认事件的日志。 将状态重置到初始状态, 并丢弃所有未经证实的事件 。
        /// 如果日志无法清除, 则丢弃 <see cref="NotSupportedException"/> 。
        /// </summary>
        protected Task ClearLogAsync(CancellationToken cancellationToken = default)
        {
            return LogViewAdaptor.ClearLogAsync(cancellationToken);
        }

        /// <summary>
        /// 当潜在的持久性或复制性协议 遇到某种连接问题时, 被调用 。
        /// <para>覆盖此数据以监测对齐一致协议和/或
        /// 将重新尝试延迟自定义。
        /// <see cref="ILogViewAdaptorFactory"/></para>将捕获和记录任何被抛出的任何例外情形。
        /// </summary>
        /// <returns>重试前等待的时间</returns>
        protected virtual void OnConnectionIssue(ConnectionIssue issue)
        {
        }

        /// <summary>
        /// 当先前报告的连接问题得到解决时,即被调用。
        /// <para>重复这一功能,以监测对齐协议的健康状况。 
        /// <see cref="ILogViewAdaptorFactory"/></para>将捕获和记录任何被抛出的任何例外情形。
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
        /// 定义如何将事件应用到状态。除非子类中被覆盖,否则它调用
        /// 状态上的动态“应用”函数,该事件为参数。
        /// 日志查看提供者将记录和记录使用这种方法提出的所有例外情形。
        /// <para>覆盖此选项可自定义如何为特定事件转换状态。</para>
        /// </summary>
        /// <param name="state">国家。</param>
        /// <param name="event">事件。</param>
        protected virtual void TransitionState(TGrainState state, TEventBase @event)
        {
            dynamic s = state;
            dynamic e = @event;
            s.Apply(e);
        }

        /// <summary>
        /// 获取日志一致性协议的适配器,该协议由日志一致性提供商安装。
        /// </summary>
        internal ILogViewAdaptor<TGrainState, TEventBase> LogViewAdaptor { get; private set; } = null!;

        /// <summary>
        /// 在 grain 构建后立即调用,安装适配器。
        /// 日志一致性提供商含有一种工厂方法,该方法用本 grain选定类型的类型构建适配器
        /// </summary>
        protected override void InstallAdaptor(ILogViewAdaptorFactory factory, object initialState, string graintypename, IGrainStorage? grainStorage, ILogConsistencyProtocolServices services)
        {
            // 调用日志一致性提供商来构建适配器,通过类型参数
            LogViewAdaptor = factory.MakeLogViewAdaptor<TGrainState, TEventBase>(this, (TGrainState)initialState, graintypename, grainStorage, services);
            (LogViewAdaptor as IInitializableLogViewAdaptor)?.EnsureConfirmedViewInitialized();
        }

        /// <summary>
        /// 如果没有指定对齐一致性提供商,则使用默认存储提供商进行储存版本化
        /// </summary>
        protected override ILogViewAdaptorFactory DefaultAdaptorFactory
        {
            get
            {
                return new StateStorage.DefaultAdaptorFactory();
            }
        }

        /// <summary>
        /// 被调适器调用,以在条目附加时更新视图。
        /// </summary>
        /// <param name="view">日志视图。</param>
        /// <param name="entry">条目。</param>
        void ILogViewAdaptorHost<TGrainState, TEventBase>.UpdateView(TGrainState view, TEventBase entry)
        {
            TransitionState(view, entry);
        }

        /// <summary>
        /// 通知激活的日志视图适配器(在用户级别 OnActivate前称为)
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
        /// 通知激活的日志视图调整器(在用户级别 OnActivate后称为)
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
        /// 通知已停止使用的日志视图适配器
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
        /// 被适应者召唤到国家变化。 
        /// </summary>
        void ILogViewAdaptorHost<TGrainState, TEventBase>.OnViewChanged(bool tentative, bool confirmed)
        {
            if (tentative)
                OnTentativeStateChanged();
            if (confirmed)
                OnStateChanged();
        }

        /// <summary>
        /// 由适应器调用, 连接问题。 
        /// </summary>
        void IConnectionIssueListener.OnConnectionIssue(ConnectionIssue connectionIssue)
        {
            OnConnectionIssue(connectionIssue);
        }

        /// <summary>
        /// 当连接问题得到解决时, 由适应器调用 。 
        /// </summary>
        void IConnectionIssueListener.OnConnectionIssueResolved(ConnectionIssue connectionIssue)
        {
            OnConnectionIssueResolved(connectionIssue);
        }
    }

}
