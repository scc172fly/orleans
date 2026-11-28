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
    /// 用于构建日志视图适配器的通用模板，这类适配器基于
    /// 顺序读写的主存储。我们使用此模板构建
    /// 多种日志一致性提供程序，它们均遵循同一基本模式：
    /// 从主存储读取最新视图或向其写入最新视图，并在写入后发送通知。
    ///<para>
    /// 注意，日志本身是临时的，即实际不保存到存储中；主存储只保存最新视图和部分
    /// 元数据（日志位置及写入标志）。
    /// 可以安全地交错调用此适配器（当然，仅限使用 Grain 调度器）。
    /// </para>
    ///<para>
    /// 子类重写 ReadAsync 和 WriteAsync，以读取或写入主存储。
    /// 对主存储的调用按顺序执行，绝不会交错执行。
    /// </para>
    /// </summary>
    /// <typeparam name="TLogView">用户定义的日志视图。</typeparam>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    /// <typeparam name="TSubmissionEntry">待处理队列中存放的提交条目的类型。</typeparam>
    public abstract class PrimaryBasedLogViewAdaptor<TLogView, TLogEntry, TSubmissionEntry> :
        ILogViewAdaptor<TLogView, TLogEntry>,
        ICancellationAwareLogViewAdaptor,
        IInitializableLogViewAdaptor
    where TLogView : class, new()
        where TLogEntry : class
        where TSubmissionEntry : SubmissionEntry<TLogEntry>
    {
        /// <summary>
        /// 将已确认视图设为初始值（空日志的视图）。
        /// </summary>
        protected abstract void InitializeConfirmedView(TLogView initialstate);

        /// <summary>
        /// 读取缓存的全局状态。
        /// </summary>
        protected abstract TLogView LastConfirmedView();

        /// <summary>
        /// 读取缓存的全局状态的版本。
        /// </summary>
        protected abstract int GetConfirmedVersion();

        /// <summary>
        /// 读取主存储中的最新状态。必须等待或重试，直到成功。
        /// 不应抛出异常，而应将异常记录到 <see cref="LastPrimaryIssue"/> 中。
        /// </summary>
        /// <returns></returns>
        protected abstract Task ReadAsync();

        /// <summary>
        /// 将待处理条目应用到主存储。必须等待或重试，直到成功。
        /// 不应抛出异常，而应将异常记录到 <see cref="LastPrimaryIssue"/> 中。
        /// </summary>
        protected abstract Task<int> WriteAsync();

        /// <summary>
        /// 为提交的日志条目创建一个提交条目。
        /// 使用类型参数，以便向此类添加特定于协议的信息。
        /// </summary>
        /// <returns></returns>
        protected abstract TSubmissionEntry MakeSubmissionEntry(TLogEntry entry);

        /// <summary>
        /// 此集群是否支持提交更新。
        /// </summary>
        protected virtual bool SupportSubmissions { get { return true; } }

        /// <summary>
        /// 处理协议消息。
        /// </summary>
        protected virtual Task<ILogConsistencyProtocolMessage> OnMessageReceived(ILogConsistencyProtocolMessage payload)
        {
            // 定义自定义协议消息的子类必须重写此方法。
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public virtual Task<IReadOnlyList<TLogEntry>> RetrieveLogSegment(int fromVersion, int length)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// 彻底清除已持久化的日志流。
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
        /// 处理通知消息。重写此方法以处理通知消息的子类型。
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

            // 子类应已在重写方法中处理此消息。
            throw new ProtocolTransportException(string.Format("message type {0} not handled by OnNotificationReceived", payload.GetType().FullName));
        }

        /// <summary>
        /// 已收到通知的最新版本号。
        /// </summary>
        private int lastVersionNotified;

        /// <summary>
        /// 在工作循环中处理已保存的通知。重写此方法以处理通知消息的子类型。
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
        /// 合并两条通知消息以进行批处理。重写此方法以处理通知消息的子类型。
        /// </summary>
        protected virtual INotificationMessage Merge(INotificationMessage earliermessage, INotificationMessage latermessage)
        {
            return new VersionNotificationMessage()
            {
                Version = latermessage.Version
            };
        }

        /// <summary>
        /// 使用此适配器的 Grain。
        /// </summary>
        protected ILogViewAdaptorHost<TLogView, TLogEntry> Host { get; private set; }

        /// <summary>
        /// 实现在不同集群的 Grain 实例之间发送通知所需的运行时服务。
        /// </summary>
        protected ILogConsistencyProtocolServices Services { get; private set; }

        /// <summary>
        /// 根据给定参数构造实例。
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

            // JournaledGrain 在构造适配器后调用此方法；基类操作会在首次使用时初始化直接构造的路径。
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

            // 此标志表示尚未从存储中执行初始加载。
            // 现在不处理此标志，而是等用户的 OnActivate 执行完毕后再处理。
            needInitialRead = true;

            Services.Log(LogLevel.Trace, "PreActivation Complete");

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task PostOnActivate()
        {
            Services.Log(LogLevel.Trace, "PostActivation Started");

            // 如果尚未启动工作程序，则启动它。
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


        // 当前已提交但尚未确认的条目。
        private readonly List<TSubmissionEntry> pending = new List<TSubmissionEntry>();


        /// 在 WriteAsync 开始时调用，以获取当前暂定状态。
        protected TLogView CopyTentativeState()
        {
            var state = TentativeView;
            tentativeStateInternal = null; // 避免引用别名共享。
            return state;
        }
        /// 在 WriteAsync 开始时调用，以获取当前批次的更新。
        protected TSubmissionEntry[] GetCurrentBatchOfUpdates()
        {
            return pending.ToArray(); // 必须使用副本。
        }
        /// 在 WriteAsync 开始时调用，以获取当前待处理更新的数量。
        protected int GetNumberPendingUpdates()
        {
            return pending.Count;
        }

        /// <summary>
        /// 暂定状态：表示稳定状态与待处理更新产生的效果之和。
        /// 延迟计算（未使用时为 null）。
        /// </summary>
        private TLogView? tentativeStateInternal;

        /// <summary>
        /// 此标志通知工作程序客户端希望刷新状态。
        /// </summary>
        private bool needRefresh;

        /// <summary>
        /// 此标志表示尚未读取过全局状态，因此应执行读取。
        /// </summary>
        private bool needInitialRead;

        /// <summary>
        /// 等待工作程序处理的清除日志请求。
        /// </summary>
        private TaskCompletionSource<bool>? clearLogRequest;
        private CancellationToken clearLogCancellationToken;

        /// <summary>
        /// 异步向领导者发送操作的后台工作程序。
        /// </summary>
        private readonly BatchWorker worker;

        private bool confirmedViewInitialized;

        /// <summary>
        /// 初始状态的缓存版本，用于初始化和重置。
        /// </summary>
        protected TLogView InitialState { init; get => Services.DeepCopy(field); }

        /// 统计数据收集。未启用统计数据收集时为 null。
        protected LogConsistencyStatistics? stats = null;


        /// 供协议使用。判断此集群是否属于已配置的多集群环境。
        protected bool IsMyClusterJoined()
        {
            return true;
        }

        /// <summary>
        /// 等待此集群加入多集群环境。
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
        /// <exception cref="ArgumentNullException"><paramref name="logEntries"/> 为 <see langword="null"/>。</exception>
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
        /// <exception cref="ArgumentNullException"><paramref name="logEntries"/> 为 <see langword="null"/>。</exception>
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
            // 创建提交条目。
            var submissionentry = this.MakeSubmissionEntry(logentry);
            submissionentry.SubmissionTime = time;
            submissionentry.ResultPromise = resultPromise;
            submissionentry.ConditionalPosition = conditionalPosition;

            // 将提交条目加入队列。
            pending.Add(submissionentry);

            // 如果正在使用暂定状态，则更新它。
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
        /// 由网络调用。
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

                // 唤醒工作程序，使其处理这些通知。
                worker.Notify();

                return null;
            }
            else
            {
                // 这是协议消息。
                return await OnMessageReceived(payLoad);
            }
        }

        /// <summary>
        /// 此方法为虚方法，以便子类添加自己的事件。
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
        /// 禁用统计数据收集。
        /// </summary>
        public void DisableStatsCollection()
        {
            stats = null;
        }

        /// <summary>
        /// 获取统计信息。（译注：原文写作 states；方法实际返回 LogConsistencyStatistics。）
        /// </summary>
        /// <returns></returns>
        public LogConsistencyStatistics? GetStats()
        {
            return stats;
        }

        [MemberNotNull(nameof(tentativeStateInternal))]
        private void CalculateTentativeState()
        {
            // 复制已确认视图。
            this.tentativeStateInternal = Services.DeepCopy(GetInitializedConfirmedView());

            // 现在应用待处理队列中的所有操作。
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
        /// 清除待处理操作并重置暂定状态。
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
        /// 批处理工作程序负责读写全局状态。
        /// 任意时刻最多只有一个工作循环处于活动状态。
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
                    needRefresh = needInitialRead = false; // 正在获取最新版本。

                    await ReadAsync();

                    NotifyViewChanges(ref version);
                }

                if (haveToWrite)
                {
                    needRefresh = needInitialRead = false; // 正在获取最新版本。

                    await UpdatePrimary();

                    if (stats != null) stats.EventCounters["WritebackEvents"]++;
                }

            }
            catch (Exception e)
            {
                // 此情况不应发生：我们本应捕获异常并将其记录
                // 在正确的位置（LastPrimaryException 或通知跟踪器）中。
                Services.ProtocolError($"Exception in Worker Cycle: {e}", true);

            }

            Services.Log(LogLevel.Debug, "<4 Done");
        }


        /// <summary>
        /// 此函数将待处理队列中的操作作为一个批次写入主存储。
        /// 持续重试，直到某个批次提交成功或没有剩余更新。
        /// </summary>
        internal async Task UpdatePrimary()
        {
            int version = GetInitializedConfirmedVersion();

            while (true)
            {
                try
                {
                    // 查找已失效的条件更新，将它们移除并通知等待者。
                    RemoveStaleConditionalUpdates();

                    if (pending.Count == 0)
                        return; // 没有要写入的更新。

                    // 尝试将更新作为一个批次写入。
                    var writeResult = await WriteAsync();

                    NotifyViewChanges(ref version, writeResult);

                    // 如果批量写入因冲突而失败，则重试。
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

                    // 通知等待中的 Promise 条件更新已成功。
                    NotifyPromises(writeResult, true);

                    // 记录稳定所需的时间，用于统计。
                    if (stats != null)
                    {
                        var timeNow = DateTime.UtcNow;
                        for (int i = 0; i < writeResult; i++)
                        {
                            var latency = timeNow - pending[i].SubmissionTime;
                            stats.StabilizationLatenciesInMsecs.Add(latency.Milliseconds);
                        }
                    }

                    // 从队列中移除已完成的更新。
                    pending.RemoveRange(0, writeResult);

                    return;
                }
                catch (Exception e)
                {
                    // 此情况不应发生：我们本应捕获异常并将其记录
                    // 在正确的位置（LastPrimaryException 或通知跟踪器）中。
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
                tentativeStateInternal = null; // 采用保守策略。
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
        /// 保存读取或更新主存储时最近发生的问题。
        /// 成功时为 null。
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
        /// 发送失败通知。（译注：此方法也发送成功通知，结果由 success 参数决定。）
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
        /// 遍历更新并移除所有已经失败的条件更新。
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
    /// 待处理队列中的提交条目的基类。
    /// </summary>
    /// <typeparam name="TLogEntry">此次提交的条目类型。</typeparam>
    public class SubmissionEntry<TLogEntry>
    {
        /// <summary>所提交的日志条目。</summary>
        [MaybeNull]
        public TLogEntry Entry;

        /// <summary>此次提交的时间戳。</summary>
        public DateTime SubmissionTime;

        /// <summary>对于条件更新，这是一个在确定更新成功与否后完成的 Promise。</summary>
        public TaskCompletionSource<bool>? ResultPromise;

        /// <summary>对于条件更新，这是应当应用该更新的日志位置。</summary>
        public int ConditionalPosition;
    }


}
