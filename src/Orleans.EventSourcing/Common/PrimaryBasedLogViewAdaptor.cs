using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orleans.EventSourcing.Common
{
    internal interface IInitializableLogViewAdaptor
    {
        void EnsureConfirmedViewInitialized();
    }

    /// <summary>
    /// 用于构建基于
    /// a 依次读写初级初级读写。我们用此来构建 
    /// 各种不同的对日一致提供商,都遵循相同的基本模式 
    /// (从主读写到主读和写最新视图,并在书面后发出通知)。
    ///<para>
    /// 请注意日志本身是短暂的, i.e.没有实际保存到存储中 - 只有最新的视图和一些 
    /// 元数据(日志位置和写标记)存储在主数据中。 
    /// 安全地将断开呼叫转到此调整器( 当然, 仅使用 grain 调度器) 。
    /// </para>
    ///<para>
    /// 子类取代 ReadAsync 和 WriteAsync 从/ 写入主字母读取 。
    /// 电话号码是序列号, i.e.永不中断。
    /// </para>
    /// </summary>
    /// <typeparam name="TLogView">对日志</typeparam>的自定义视图
    /// <typeparam name="TLogEntry">日志条目类型</typeparam>
    /// <typeparam name="TSubmissionEntry">储存在待决队列中的提交条目类型</typeparam>
    public abstract class PrimaryBasedLogViewAdaptor<TLogView, TLogEntry, TSubmissionEntry> :
        ILogViewAdaptor<TLogView, TLogEntry>,
        ICancellationAwareLogViewAdaptor,
        IInitializableLogViewAdaptor
    where TLogView : class, new()
        where TLogEntry : class
        where TSubmissionEntry : SubmissionEntry<TLogEntry>
    {
        /// <summary>
        /// 设定已确认的视图初始值( 空日志的视图)
        /// </summary>
        protected abstract void InitializeConfirmedView(TLogView initialstate);

        /// <summary>
        /// 读取隐藏全球状态 。
        /// </summary>
        protected abstract TLogView LastConfirmedView();

        /// <summary>
        /// 读取全球缓存状态版本 。
        /// </summary>
        protected abstract int GetConfirmedVersion();

        /// <summary>
        /// 读取最新的初级状态。 必须屏蔽/ 回写直到成功 。
        /// 绝不要加以例外,但你们应当记录在册上。 <see cref="LastPrimaryIssue"/>
        /// </summary>
        /// <returns></returns>
        protected abstract Task ReadAsync();

        /// <summary>
        /// 应用未完成的条目到主机。 必须块/ 回写直到成功 。 
        /// 绝不要加以例外,但你们应当记录在册上。 <see cref="LastPrimaryIssue"/>
        /// </summary>
        protected abstract Task<int> WriteAsync();

        /// <summary>
        /// 为提交的日志条目创建提交条目 。 
        /// 使用类型参数, 这样我们就可以添加协议特定信息到此类 。
        /// </summary>
        /// <returns></returns>
        protected abstract TSubmissionEntry MakeSubmissionEntry(TLogEntry entry);

        /// <summary>
        /// 此集是否支持提交更新
        /// </summary>
        protected virtual bool SupportSubmissions { get { return true; } }

        /// <summary>
        /// 处理协议信息 。
        /// </summary>
        protected virtual Task<ILogConsistencyProtocolMessage> OnMessageReceived(ILogConsistencyProtocolMessage payload)
        {
            // 定义自定义协议消息的子类必须覆盖此子类
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public virtual Task<IReadOnlyList<TLogEntry>> RetrieveLogSegment(int fromVersion, int length)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 完全清除持续日志流 。
        /// </summary>
        protected virtual Task ClearPrimaryLogAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public virtual Task ClearLogAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (clearLogRequest is null || clearLogRequest.Task.IsCompleted)
            {
                clearLogCancellationToken = cancellationToken;
                clearLogRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            worker.Notify();

            var clearLogTask = clearLogRequest.Task;

            return cancellationToken.CanBeCanceled
                ? clearLogTask.WaitAsync(cancellationToken)
                : clearLogTask;
        }

        /// <summary>
        /// 处理通知信息。 覆盖此选项可处理通知子类型 。
        /// </summary>
        protected virtual void OnNotificationReceived(INotificationMessage payload)
        {
            var msg = payload as VersionNotificationMessage;
            if (msg != null)
            {
                if (msg.Version > lastVersionNotified)
                    lastVersionNotified = msg.Version;
                return;
            }

            var batchmsg = payload as BatchedNotificationMessage;
            if (batchmsg != null)
            {
                foreach (var bm in batchmsg.Notifications)
                    OnNotificationReceived(bm);
                return;
            }

            // 子类应该已经在覆盖中处理此操作
            throw new ProtocolTransportException(string.Format("message type {0} not handled by OnNotificationReceived", payload.GetType().FullName));
        }

        /// <summary>
        /// 我们已获悉,我们最后的版本是
        /// </summary>
        private int lastVersionNotified;

        /// <summary>
        /// 工人周期内储存的工艺通知处理程序,处理通知子类型。
        /// </summary>
        protected virtual void ProcessNotifications()
        {
            if (lastVersionNotified > GetInitializedConfirmedVersion())
            {
                Services.Log(LogLevel.Debug, "force refresh because of version notification v{0}", lastVersionNotified);
                needRefresh = true;
            }
        }

        /// <summary>
        /// 合并两个通知信息,用于批量。覆盖处理通知子类型。
        /// </summary>
        protected virtual INotificationMessage Merge(INotificationMessage earliermessage, INotificationMessage latermessage)
        {
            return new VersionNotificationMessage()
            {
                Version = latermessage.Version
            };
        }

        /// <summary>
        /// 使用此适配器的 grain 。
        /// </summary>
        protected ILogViewAdaptorHost<TLogView, TLogEntry> Host { get; private set; }

        /// <summary>
        /// 在不同的组别中执行 grain 通知之间时间服务。
        /// </summary>
        protected ILogConsistencyProtocolServices Services { get; private set; }

        /// <summary>
        /// 构造给定参数的示例。
        /// </summary>
        protected PrimaryBasedLogViewAdaptor(
            ILogViewAdaptorHost<TLogView, TLogEntry> host,
            TLogView initialstate,
            ILogConsistencyProtocolServices services)
        {
            Debug.Assert(host != null && services != null && initialstate != null);
            this.Host = host;
            this.Services = services;
            this.InitialState = Services.DeepCopy(initialstate);
            worker = new BatchWorkerFromDelegate(Work);
        }

        private void EnsureConfirmedViewInitialized()
        {
            if (confirmedViewInitialized)
            {
                return;
            }

            // JournaledGrain 在调整器建造后调用此功能;基础业务初始化了首次使用的直接开工道路。
            InitializeConfirmedView(InitialState);
            confirmedViewInitialized = true;
        }

        void IInitializableLogViewAdaptor.EnsureConfirmedViewInitialized() => EnsureConfirmedViewInitialized();

        private TLogView GetInitializedConfirmedView()
        {
            EnsureConfirmedViewInitialized();
            return LastConfirmedView();
        }

        private int GetInitializedConfirmedVersion()
        {
            EnsureConfirmedViewInitialized();
            return GetConfirmedVersion();
        }

        /// <inheritdoc/>
        public virtual Task PreOnActivate()
        {
            EnsureConfirmedViewInitialized();

            Services.Log(LogLevel.Trace, "PreActivation Started");

            // 表示我们还没有从存储中做起初始负荷
            // 我们还没有对此采取行动,但等到用户完成后再行动 OnActivate 已经跑了。 
            needInitialRead = true;

            Services.Log(LogLevel.Trace, "PreActivation Complete");

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task PostOnActivate()
        {
            Services.Log(LogLevel.Trace, "PostActivation Started");

            // 启动工人, 如果尚未发生的话
            if (needInitialRead)
                worker.Notify();

            Services.Log(LogLevel.Trace, "PostActivation Complete");

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public virtual Task PostOnDeactivate() => PostOnDeactivateCore(CancellationToken.None);

        Task ICancellationAwareLogViewAdaptor.PostOnDeactivate(CancellationToken cancellationToken) =>
            PostOnDeactivateCore(cancellationToken);

        private async Task PostOnDeactivateCore(CancellationToken cancellationToken)
        {
            Services.Log(LogLevel.Trace, "Deactivation Started");

            while (!worker.IsIdle())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
                await worker.WaitForCurrentWorkToBeServiced().WaitAsync(cancellationToken);
            }

            Services.Log(LogLevel.Trace, "Deactivation Complete");
        }


        // 未经证实的条目。 
        private readonly List<TSubmissionEntry> pending = new List<TSubmissionEntry>();


        /// 调用当前临时状态的 WriteAsync
        protected TLogView CopyTentativeState()
        {
            var state = TentativeView;
            tentativeStateInternal = null; // 以避免别名
            return state;
        }
        /// 调用当前批次更新的 WriteAsync 开始时
        protected TSubmissionEntry[] GetCurrentBatchOfUpdates()
        {
            return pending.ToArray(); // 必须使用复制件
        }
        /// 调用 WriteAsync 开始时,以获取当前待更新的数
        protected int GetNumberPendingUpdates()
        {
            return pending.Count;
        }

        /// <summary>
        ///  暂定状态:代表稳定国家+待更新的影响。
        ///  低酸(未使用时不得使用)
        /// </summary>
        private TLogView? tentativeStateInternal;

        /// <summary>
        /// 国旗向工人表明客户想更新国家
        /// </summary>
        private bool needRefresh;

        /// <summary>
        /// 旗帜表明我们还没有读过全球国家,应该阅读
        /// </summary>
        private bool needInitialRead;

        /// <summary>
        /// 工人提出有待处理的澄清申请。
        /// </summary>
        private TaskCompletionSource<bool>? clearLogRequest;
        private CancellationToken clearLogCancellationToken;

        /// <summary>
        /// 向领导者不同步发送行动的背景工作者
        /// </summary>
        private readonly BatchWorker worker;

        private bool confirmedViewInitialized;

        /// <summary>
        /// 初始化时使用的初始状态的缓存版本。 并用于重设 。
        /// </summary>
        protected TLogView InitialState { init; get => Services.DeepCopy(field); }

        /// 统计正在收集。 除非启用数据库收藏, 否则无效 。
        protected LogConsistencyStatistics? stats = null;


        /// 用于协议。 确定该组是否是配置的多组集的一部分 。
        protected bool IsMyClusterJoined()
        {
            return true;
        }

        /// <summary>
        /// 块块, 直到此组被加入多组群 。
        /// </summary>
        protected async Task EnsureClusterJoinedAsync()
        {
            while (!IsMyClusterJoined())
            {
                Services.Log(LogLevel.Debug, "Waiting for join");
                await Task.Delay(5000);
            }
        }

        /// <inheritdoc />
        public void Submit(TLogEntry logEntry)
        {
            EnsureConfirmedViewInitialized();

            if (!SupportSubmissions)
                throw new InvalidOperationException("provider does not support submissions on cluster " + Services.MyClusterId);

            if (stats != null) stats.EventCounters["SubmitCalled"]++;

            Services.Log(LogLevel.Trace, "Submit");

            SubmitInternal(DateTime.UtcNow, logEntry);

            worker.Notify();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="logEntries"/> 是 <see langword="null"/>.</exception>
        public void SubmitRange(IEnumerable<TLogEntry> logEntries)
        {
            ArgumentNullException.ThrowIfNull(logEntries);
            EnsureConfirmedViewInitialized();

            if (!SupportSubmissions)
                throw new InvalidOperationException("Provider does not support submissions on cluster " + Services.MyClusterId);

            if (stats != null) stats.EventCounters["SubmitRangeCalled"]++;

            Services.Log(LogLevel.Trace, "SubmitRange");

            var time = DateTime.UtcNow;

            foreach (var e in logEntries)
                SubmitInternal(time, e);

            worker.Notify();
        }

        /// <inheritdoc />
        public Task<bool> TryAppend(TLogEntry logEntry)
        {
            EnsureConfirmedViewInitialized();

            if (!SupportSubmissions)
                throw new InvalidOperationException("Provider does not support submissions on cluster " + Services.MyClusterId);

            if (stats != null) stats.EventCounters["TryAppendCalled"]++;

            Services.Log(LogLevel.Trace, "TryAppend");

            var promise = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            SubmitInternal(DateTime.UtcNow, logEntry, GetInitializedConfirmedVersion() + pending.Count, promise);

            worker.Notify();

            return promise.Task;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="logEntries"/> 是 <see langword="null"/>.</exception>
        public Task<bool> TryAppendRange(IEnumerable<TLogEntry> logEntries)
        {
            ArgumentNullException.ThrowIfNull(logEntries);
            EnsureConfirmedViewInitialized();

            if (!SupportSubmissions)
                throw new InvalidOperationException("Provider does not support submissions on cluster " + Services.MyClusterId);

            if (stats != null) stats.EventCounters["TryAppendRangeCalled"]++;

            Services.Log(LogLevel.Trace, "TryAppendRange");

            var promise = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var time = DateTime.UtcNow;
            var pos = GetInitializedConfirmedVersion() + pending.Count;

            bool first = true;
            foreach (var e in logEntries)
            {
                SubmitInternal(time, e, pos++, first ? promise : null);
                first = false;
            }

            if (first)
                promise.SetResult(true);

            worker.Notify();

            return promise.Task;
        }


        private const int unconditional = -1;

        private void SubmitInternal(DateTime time, TLogEntry logentry, int conditionalPosition = unconditional, TaskCompletionSource<bool>? resultPromise = null)
        {
            // 创建提交条目
            var submissionentry = this.MakeSubmissionEntry(logentry);
            submissionentry.SubmissionTime = time;
            submissionentry.ResultPromise = resultPromise;
            submissionentry.ConditionalPosition = conditionalPosition;

            // 将提交文件添加到队列
            pending.Add(submissionentry);

            // 如果我们有一个暂定使用状态, 请更新
            if (this.tentativeStateInternal != null)
            {
                try
                {
                    Host.UpdateView(this.tentativeStateInternal, logentry);
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("UpdateView", nameof(SubmitInternal), e);
                }
            }

            try
            {
                Host.OnViewChanged(true, false);
            }
            catch (Exception e)
            {
                Services.CaughtUserCodeException("OnViewChanged", nameof(SubmitInternal), e);
            }
        }

        /// <inheritdoc />
        public TLogView TentativeView
        {
            get
            {
                if (stats != null)
                    stats.EventCounters["TentativeViewCalled"]++;

                if (tentativeStateInternal == null)
                    CalculateTentativeState();

                return tentativeStateInternal;
            }
        }

        /// <inheritdoc />
        public TLogView ConfirmedView
        {
            get
            {
                if (stats != null)
                    stats.EventCounters["ConfirmedViewCalled"]++;

                return GetInitializedConfirmedView();
            }
        }

        /// <inheritdoc />
        public int ConfirmedVersion
        {
            get
            {
                if (stats != null)
                    stats.EventCounters["ConfirmedVersionCalled"]++;

                return GetInitializedConfirmedVersion();
            }
        }

        /// <summary>
        /// 网络呼叫
        /// </summary>
        /// <param name="payLoad"></param>
        /// <returns></returns>
        public async Task<ILogConsistencyProtocolMessage?> OnProtocolMessageReceived(ILogConsistencyProtocolMessage payLoad)
        {
            EnsureConfirmedViewInitialized();

            var notificationMessage = payLoad as INotificationMessage;

            if (notificationMessage != null)
            {
                Services.Log(LogLevel.Debug, "NotificationReceived v{0}", notificationMessage.Version);

                OnNotificationReceived(notificationMessage);

                // 工人,所以它会处理通知
                worker.Notify();

                return null;
            }
            else
            {
                //这是协议信息
                return await OnMessageReceived(payLoad);
            }
        }

        /// <summary>
        /// 方法为虚拟,所以子类可以添加自己的事件
        /// </summary>
        public virtual void EnableStatsCollection()
        {

            stats = new LogConsistencyStatistics()
            {
                EventCounters = new Dictionary<string, long>(),
                StabilizationLatenciesInMsecs = new List<int>()
            };

            stats.EventCounters.Add("TentativeViewCalled", 0);
            stats.EventCounters.Add("ConfirmedViewCalled", 0);
            stats.EventCounters.Add("ConfirmedVersionCalled", 0);
            stats.EventCounters.Add("SubmitCalled", 0);
            stats.EventCounters.Add("SubmitRangeCalled", 0);
            stats.EventCounters.Add("TryAppendCalled", 0);
            stats.EventCounters.Add("TryAppendRangeCalled", 0);
            stats.EventCounters.Add("ConfirmSubmittedEntriesCalled", 0);
            stats.EventCounters.Add("SynchronizeNowCalled", 0);

            stats.EventCounters.Add("WritebackEvents", 0);

            stats.StabilizationLatenciesInMsecs = new List<int>();
        }

        /// <summary>
        /// 禁用统计收藏
        /// </summary>
        public void DisableStatsCollection()
        {
            stats = null;
        }

        /// <summary>
        /// 获取状态
        /// </summary>
        /// <returns></returns>
        public LogConsistencyStatistics? GetStats()
        {
            return stats;
        }

        [MemberNotNull(nameof(tentativeStateInternal))]
        private void CalculateTentativeState()
        {
            // 确认视图的复制件
            this.tentativeStateInternal = Services.DeepCopy(GetInitializedConfirmedView());

            // 现在应用所有待处理操作 
            foreach (var u in this.pending)
                try
                {
                    Host.UpdateView(this.tentativeStateInternal, u.Entry!);
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("UpdateView", nameof(CalculateTentativeState), e);
                }
        }

        /// <summary>
        /// 清除待处理的操作并重设暂定状态。
        /// </summary>
        internal void ResetTentativeState()
        {
            this.pending.Clear();

            CalculateTentativeState();
        }

        private async Task ProcessClearLogRequest()
        {
            var clearLogTask = clearLogRequest;
            if (clearLogTask is null || clearLogTask.Task.IsCompleted)
                return;

            try
            {
                clearLogCancellationToken.ThrowIfCancellationRequested();

                await ClearPrimaryLogAsync(clearLogCancellationToken);

                InitializeConfirmedView(this.InitialState);
                ResetTentativeState();
                needRefresh = needInitialRead = false;
                lastVersionNotified = 0;

                try
                {
                    Host.OnViewChanged(true, true);
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("OnViewChanged", nameof(ProcessClearLogRequest), e);
                }

                clearLogTask.TrySetResult(true);
            }
            catch (OperationCanceledException) when (clearLogCancellationToken.IsCancellationRequested)
            {
                clearLogTask.TrySetCanceled(clearLogCancellationToken);
            }
            catch (Exception exception)
            {
                clearLogTask.TrySetException(exception);
            }
            finally
            {
                if (ReferenceEquals(clearLogRequest, clearLogTask))
                    clearLogRequest = null;
            }
        }


        /// <summary>
        /// 批量工人的表演从全球状态开始,并写成全球状态。
        /// 在任何时候,只有一个工作周期在运行。
        /// </summary>
        internal async Task Work()
        {
            EnsureConfirmedViewInitialized();

            await ProcessClearLogRequest();

            Services.Log(LogLevel.Debug, "<1 ProcessNotifications");

            var version = GetInitializedConfirmedVersion();

            ProcessNotifications();

            Services.Log(LogLevel.Debug, "<2 NotifyViewChanges");

            NotifyViewChanges(ref version);

            bool haveToWrite = (pending.Count != 0);

            bool haveToRead = needInitialRead || (needRefresh && !haveToWrite);

            Services.Log(LogLevel.Debug, "<3 Storage htr={0} htw={1}", haveToRead, haveToWrite);

            try
            {
                if (haveToRead)
                {
                    needRefresh = needInitialRead = false; // 正在获取新版本

                    await ReadAsync();

                    NotifyViewChanges(ref version);
                }

                if (haveToWrite)
                {
                    needRefresh = needInitialRead = false; // 正在获取新版本

                    await UpdatePrimary();

                    if (stats != null) stats.EventCounters["WritebackEvents"]++;
                }

            }
            catch (Exception e)
            {
                // 绝不应该发生这种情况——我们本该抓和储存例外 
                // 正确地点(LastPrimaryException 或通知跟踪器)
                Services.ProtocolError($"Exception in Worker Cycle: {e}", true);

            }

            Services.Log(LogLevel.Debug, "<4 Done");
        }


        /// <summary>
        /// 此函数将待决队列中的操作作为批次存储到主队列中 。
        /// 重试,直到某批货物投入或没有更新。
        /// </summary>
        internal async Task UpdatePrimary()
        {
            int version = GetInitializedConfirmedVersion();

            while (true)
            {
                try
                {
                    // 查找已过期的有条件更新条件, 删除它们并通知服务员
                    RemoveStaleConditionalUpdates();

                    if (pending.Count == 0)
                        return; // 没有要写入的更新 。

                    // 尝试将更新作为批数写入
                    var writeResult = await WriteAsync();

                    NotifyViewChanges(ref version, writeResult);

                    // 如果批次写成因冲突而失败, 请重试 。
                    if (writeResult == 0)
                        continue;

                    try
                    {
                        Host.OnViewChanged(false, true);
                    }
                    catch (Exception e)
                    {
                        Services.CaughtUserCodeException("OnViewChanged", nameof(UpdatePrimary), e);
                    }

                    // 有条件更新成功通知通知了等待承诺
                    NotifyPromises(writeResult, true);

                    // 统计稳定时间
                    if (stats != null)
                    {
                        var timeNow = DateTime.UtcNow;
                        for (int i = 0; i < writeResult; i++)
                        {
                            var latency = timeNow - pending[i].SubmissionTime;
                            stats.StabilizationLatenciesInMsecs.Add(latency.Milliseconds);
                        }
                    }

                    // 从队列中删除已完成的更新
                    pending.RemoveRange(0, writeResult);

                    return;
                }
                catch (Exception e)
                {
                    // 绝不应该发生这种情况——我们本该抓和储存例外 
                    // 正确地点(LastPrimaryException 或通知跟踪器)
                    Services.ProtocolError($"Exception in {nameof(UpdatePrimary)}: {e}", true);
                }
            }
        }


        private void NotifyViewChanges(ref int version, int numWritten = 0)
        {
            var v = GetInitializedConfirmedVersion();
            bool tentativeChanged = (v != version + numWritten);
            bool confirmedChanged = (v != version);
            if (tentativeChanged || confirmedChanged)
            {
                tentativeStateInternal = null; // 保守的。
                try
                {
                    Host.OnViewChanged(tentativeChanged, confirmedChanged);
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("OnViewChanged", nameof(NotifyViewChanges), e);
                }
                version = v;
            }
        }

        /// <summary>
        /// 存储在读取或更新小学时发生的最后一个问题。
        /// 无效,如果成功。
        /// </summary>
        protected RecordedConnectionIssue LastPrimaryIssue;



        /// <inheritdoc />
        public async Task Synchronize()
        {
            if (stats != null)
                stats.EventCounters["SynchronizeNowCalled"]++;

            Services.Log(LogLevel.Debug, "SynchronizeNowStart");

            needRefresh = true;
            await worker.NotifyAndWaitForWorkToBeServiced();

            Services.Log(LogLevel.Debug, "SynchronizeNowComplete");
        }

        /// <inheritdoc/>
        public IEnumerable<TLogEntry> UnconfirmedSuffix
        {
            get
            {
                return pending.Select(te => te.Entry!);
            }
        }

        /// <inheritdoc />
        public async Task ConfirmSubmittedEntries()
        {
            if (stats != null)
                stats.EventCounters["ConfirmSubmittedEntriesCalled"]++;

            Services.Log(LogLevel.Debug, "ConfirmSubmittedEntriesStart");

            if (pending.Count != 0)
                await worker.WaitForCurrentWorkToBeServiced();

            Services.Log(LogLevel.Debug, "ConfirmSubmittedEntriesEnd");
        }

        /// <summary>
        /// 发送失败通知
        /// </summary>
        protected void NotifyPromises(int count, bool success)
        {
            for (int i = 0; i < count; i++)
            {
                var promise = pending[i].ResultPromise;
                if (promise != null)
                    promise.SetResult(success);
            }
        }

        /// <summary>
        /// 检查更新并删除所有已经失败的有条件更新
        /// </summary>
        protected void RemoveStaleConditionalUpdates()
        {
            int version = GetInitializedConfirmedVersion();
            bool foundFailedConditionalUpdates = false;

            for (int pos = 0; pos < pending.Count; pos++)
            {
                var submissionEntry = pending[pos];
                if (submissionEntry.ConditionalPosition != unconditional
                    && (foundFailedConditionalUpdates ||
                           submissionEntry.ConditionalPosition != (version + pos)))
                {
                    foundFailedConditionalUpdates = true;
                    if (submissionEntry.ResultPromise != null)
                        submissionEntry.ResultPromise.SetResult(false);
                }
            }

            if (foundFailedConditionalUpdates)
            {
                pending.RemoveAll(e => e.ConditionalPosition != unconditional);
                tentativeStateInternal = null;
                try
                {
                    Host.OnViewChanged(true, false);
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("OnViewChanged", nameof(RemoveStaleConditionalUpdates), e);
                }
            }
        }
    }

    /// <summary>
    /// 存档文件条目的基础类,存储在待处理队列中。 
    /// </summary>
    /// <typeparam name="TLogEntry">提交材料条目类型</typeparam>
    public class SubmissionEntry<TLogEntry>
    {
        /// <summary> 提交的日志条目 。 </summary>
        [MaybeNull]
        public TLogEntry Entry;

        /// <summary> 提交文件的时间戳 。 </summary>
        public DateTime SubmissionTime;

        /// <summary> 对于有条件更新,一旦知道更新是否成功就解决承诺问题。</summary>
        public TaskCompletionSource<bool>? ResultPromise;

        /// <summary> 对于有条件更新,应应用此更新的日志位置。 </summary>
        public int ConditionalPosition;
    }


}
