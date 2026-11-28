using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.CodeGeneration;
using Orleans.GrainReferences;
using Orleans.Hosting;
using Orleans.Internal;
using Orleans.Metadata;
using Orleans.Reminders;
using Orleans.Reminders.Diagnostics;
using Orleans.Runtime.ConsistentRing;
using Orleans.Runtime.Internal;
using Orleans.Runtime.MembershipService;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.ReminderService
{
    internal sealed partial class LocalReminderService : GrainService, IReminderService, ILifecycleParticipant<ISiloLifecycle>, IGrainServiceRangeChangeQueue
    {
        private const int InitialReadRetryCountBeforeFastFailForUpdates = 2;
        private static readonly TimeSpan InitialReadMaxWaitTimeForUpdates = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan InitialReadRetryPeriod = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MinimumReminderDueTime = TimeSpan.FromMilliseconds(1);
        private readonly ILogger logger;
        private readonly ReminderOptions reminderOptions;
        private readonly Dictionary<ReminderIdentity, LocalReminderData> localReminders = new();
        private readonly IReminderTable reminderTable;
        private readonly TaskCompletionSource<bool> startedTask;
        private readonly IAsyncTimer listRefreshTimer; // 刷新本地提醒器列表以反映全局提醒器表的定时器。
        private readonly GrainReferenceActivator _referenceActivator;
        private readonly GrainInterfaceType _grainInterfaceType;
        private readonly SiloStatusListenerManager _siloStatusListenerManager;
        private readonly TimeProvider _timeProvider;
        private readonly ReminderInstruments _reminderInstruments;
        private long localTableSequence;
        // 测试屏障在调度器之外读取此状态，使得服务繁忙时仍能观察该状态。
        private readonly object _reconciliationLock = new();
        private long reconciliationGeneration;
        private TaskCompletionSource reconciliationGenerationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task reconciliationTask = Task.CompletedTask;
        private uint initialReadCallCount = 0;
        private Task? runTask;
        private readonly object _deliveryLock = new();
        private bool _isDeliveringReminders;
        private AdmissionGate? _deliveryGate;
        private Task _deliveryStopped = Task.CompletedTask;

        public LocalReminderService(
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            IReminderTable reminderTable,
            IAsyncTimerFactory asyncTimerFactory,
            IOptions<ReminderOptions> reminderOptions,
            IConsistentRingProvider ringProvider,
            SiloStatusListenerManager siloStatusListenerManager,
            [FromKeyedServices(ReminderTimeProviderNames.Reminders)] TimeProvider timeProvider,
            ReminderInstruments reminderInstruments,
            SystemTargetShared shared)
            : base(
                  SystemTargetGrainId.CreateGrainServiceGrainId(GrainInterfaceUtils.GetGrainClassTypeCode(typeof(IReminderService)), null!, shared.SiloAddress),
                  ringProvider,
                  shared)
        {
            _referenceActivator = referenceActivator;
            _grainInterfaceType = interfaceTypeResolver.GetGrainInterfaceType(typeof(IRemindable));
            _siloStatusListenerManager = siloStatusListenerManager;
            this.reminderOptions = reminderOptions.Value;
            this.reminderTable = reminderTable;
            _timeProvider = timeProvider;
            _reminderInstruments = reminderInstruments;
            _reminderInstruments.RegisterActiveRemindersObserve(() => localReminders.Count);
            startedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.logger = shared.LoggerFactory.CreateLogger<LocalReminderService>();
            this.listRefreshTimer = asyncTimerFactory.Create(this.reminderOptions.RefreshReminderListPeriod, "ReminderService.ReminderListRefresher", _timeProvider);
            shared.ActivationDirectory.RecordNewTarget(this);
        }

        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle observer)
        {
            observer.Subscribe(
                nameof(LocalReminderService),
                ServiceLifecycleStage.RuntimeGrainServices,
                StartReminderTable,
                StopReminderTable);

            async Task StartReminderTable(CancellationToken ct)
            {
                try
                {
                    await this.QueueTask(() => StartReminderTableCoreAsync(ct));
                }
                catch (Exception exception)
                {
                    LogErrorActivatingReminderService(exception);
                    throw;
                }

                async Task StartReminderTableCoreAsync(CancellationToken cancellationToken)
                {
                    CheckRuntimeContext();

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(this.reminderOptions.InitializationTimeout);

                    // 确认能够访问底层存储，因为此后 ReminderService 将在后台加载，无法再阻止 Silo 启动。
                    await reminderTable.StartAsync(cts.Token);
                }
            }

            async Task StopReminderTable(CancellationToken ct)
            {
                try
                {
                    await this.QueueTask(StopReminderServiceAndTable).WaitAsync(ct);
                }
                catch (Exception exception)
                {
                    LogErrorStoppingReminderService(exception);
                    throw;
                }

                async Task StopReminderServiceAndTable()
                {
                    await StopReminderService();
                    await reminderTable.StopAsync(ct);
                }
            }
        }

        public override Task Start() => Start(CancellationToken.None);

        public async Task Start(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckRuntimeContext();

            while (true)
            {
                Task deliveryStopped;
                lock (_deliveryLock)
                {
                    if (_isDeliveringReminders)
                    {
                        return;
                    }

                    deliveryStopped = _deliveryStopped;
                    if (deliveryStopped.IsCompletedSuccessfully)
                    {
                        _deliveryGate = new();
                        _isDeliveringReminders = true;
                        break;
                    }
                }

                await deliveryStopped.WaitAsync(cancellationToken);
            }

            try
            {
                foreach (var reminderData in localReminders.Values)
                {
                    reminderData.TryStart();
                }

                await base.Start();
            }
            catch (Exception exception)
            {
                await StopReminderService();
                LogErrorStartingReminderService(exception);
                throw;
            }
        }

        public override Task Stop() => Stop(CancellationToken.None);

        public async Task Stop(CancellationToken cancellationToken)
        {
            CheckRuntimeContext();
            await StopDeliveringReminders().WaitAsync(cancellationToken);
        }

        private Task StopDeliveringReminders()
        {
            lock (_deliveryLock)
            {
                if (!_isDeliveringReminders)
                {
                    return _deliveryStopped;
                }

                _isDeliveringReminders = false;
                return _deliveryStopped = StopDeliveringRemindersCore(_deliveryGate!.CloseAsync());
            }

            async Task StopDeliveringRemindersCore(Task deliveryQuiesced)
            {
                await deliveryQuiesced;

                // 停止所有提醒器。
                var tasks = new List<Task>(localReminders.Count);
                foreach (var reminderData in localReminders.Values)
                {
                    tasks.Add(reminderData.StopAsync(ReminderEvents.LocalReminderStopReason.ServiceStopped));
                }

                await Task.WhenAll(tasks);
            }
        }

        private async Task StopReminderService()
        {
            await StopDeliveringReminders();
            await base.Stop();

            listRefreshTimer.Dispose();
            if (this.runTask is { } task)
            {
                await task;
            }

            // 正常关闭时，还应将提醒器的负责权移交给新的负责方，并更新 ReminderTable。
            // 当前通过定期读取提醒器表完成此工作。
        }

        public async Task<IGrainReminder> RegisterOrUpdateReminder(GrainId grainId, string reminderName, TimeSpan dueTime, TimeSpan period)
            => await RegisterOrUpdateReminder(grainId, reminderName, dueTime, period, CancellationToken.None);

        public async Task<IGrainReminder> RegisterOrUpdateReminder(
            GrainId grainId,
            string reminderName,
            TimeSpan dueTime,
            TimeSpan period,
            CancellationToken cancellationToken)
        {
            CheckRuntimeContext();

            var entry = new ReminderEntry
            {
                GrainId = grainId,
                ReminderName = reminderName,
                StartAt = _timeProvider.GetUtcNow().UtcDateTime.Add(dueTime),
                Period = period,
            };

            LogDebugRegisterOrUpdateReminder(entry);
            cancellationToken.ThrowIfCancellationRequested();
            await DoResponsibilitySanityCheck(grainId, "RegisterReminder").WaitAsync(cancellationToken);
            string? newEtag = await reminderTable.UpsertRow(entry, cancellationToken);

            if (newEtag != null)
            {
                entry.ETag = newEtag;
                // 请求可能到达过时的负责方。在此处将其持久化，但让当前负责方加载它。
                if (RingRange.InRange(grainId))
                {
                    ReconcileLocalReminder(entry, _timeProvider.GetUtcNow().UtcDateTime);
                }

                LogDebugRegisterReminder(entry, localTableSequence);

                if (logger.IsEnabled(LogLevel.Trace)) PrintReminders();
                var reminder = new ReminderData(grainId, reminderName, newEtag);
                ReminderEvents.EmitRegistered(grainId, reminderName, Silo);

                return reminder;
            }

            LogErrorRegisterReminder(entry);
            throw new ReminderException($"Could not register reminder {entry} to reminder table due to a race. Please try again later.");
        }

        /// <summary>
        /// 在本地停止提醒器，并从外部存储系统中移除它。
        /// </summary>
        /// <param name="reminder"></param>
        /// <returns></returns>
        public async Task UnregisterReminder(IGrainReminder reminder)
            => await UnregisterReminder(reminder, CancellationToken.None);

        public async Task UnregisterReminder(IGrainReminder reminder, CancellationToken cancellationToken)
        {
            CheckRuntimeContext();

            var remData = (ReminderData)reminder;
            LogDebugUnregisterReminder(reminder, localTableSequence);

            var grainId = remData.GrainId;
            string reminderName = remData.ReminderName;
            string eTag = remData.ETag;

            cancellationToken.ThrowIfCancellationRequested();
            await DoResponsibilitySanityCheck(grainId, "RemoveReminder").WaitAsync(cancellationToken);

            // 本地可能不存在此提醒器；即便如此，仍尝试将它从提醒器
            // 表移除。定期刷新机制会在可能于本地持有该提醒器的任何 Silo 的 LocalReminderService 中停止它。

            // 从持久化或内存存储中移除。
            var success = await reminderTable.RemoveRow(grainId, reminderName, eTag, cancellationToken);
            if (!success)
            {
                success = await IsReminderAlreadyRemoved(grainId, reminderName, reminder, cancellationToken);
            }

            if (success)
            {
                var key = new ReminderIdentity(grainId, reminderName);
                if (localReminders.TryGetValue(key, out var localRem))
                {
                    RequestLocalReminderRemoval(key, localRem, ReminderEvents.LocalReminderStopReason.Unregistered);
                    LogStoppedReminder(reminder);
                    if (logger.IsEnabled(LogLevel.Trace)) PrintReminders($"After removing {reminder}.");
                }
                else
                {
                    AddLocalReminderTombstone(key, ReminderEvents.LocalReminderStopReason.Unregistered);
                    LogRemovedReminderFromTable(reminder);
                }
                ReminderEvents.EmitUnregistered(grainId, reminderName, Silo);
            }
            else
            {
                LogErrorUnregisterReminder(reminder);
                throw new ReminderException($"Could not unregister reminder {reminder} from the reminder table, due to tag mismatch. You can retry.");
            }
        }

        private async Task<bool> IsReminderAlreadyRemoved(GrainId grainId, string reminderName, IGrainReminder reminder, CancellationToken cancellationToken)
        {
            if (await reminderTable.ReadRow(grainId, reminderName, cancellationToken) is not null)
            {
                return false;
            }

            LogDebugReminderAlreadyRemoved(reminder);
            return true;
        }

        private void ObserveLocalReminderStop(Task stopTask, GrainId grainId, string reminderName)
        {
            ArgumentNullException.ThrowIfNull(stopTask);

            stopTask.ContinueWith(
                static (task, state) =>
                {
                    var (service, grainId, reminderName) = ((LocalReminderService Service, GrainId GrainId, string ReminderName))state!;
                    service.LogErrorStoppingLocalReminder(task.Exception!, grainId, reminderName);
                },
                (this, grainId, reminderName),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public async Task<IGrainReminder?> GetReminder(GrainId grainId, string reminderName)
            => await GetReminder(grainId, reminderName, CancellationToken.None);

        public async Task<IGrainReminder?> GetReminder(GrainId grainId, string reminderName, CancellationToken cancellationToken)
        {
            LogDebugGetReminder(grainId, reminderName);
            ReminderEntry? entry = await reminderTable.ReadRow(grainId, reminderName, cancellationToken);
            return entry?.ToIGrainReminder();
        }

        async Task<IGrainReminder?> IReminderService.GetReminder(GrainId grainId, string reminderName)
            => await GetReminder(grainId, reminderName);

        public async Task<List<IGrainReminder>> GetReminders(GrainId grainId)
            => await GetReminders(grainId, CancellationToken.None);

        public async Task<List<IGrainReminder>> GetReminders(GrainId grainId, CancellationToken cancellationToken)
        {
            LogDebugGetReminders(grainId);
            var tableData = await reminderTable.ReadRows(grainId, cancellationToken);
            return tableData.Reminders.Select(entry => entry.ToIGrainReminder()).ToList();
        }

        /// <summary>
        /// 尝试从全局提醒器表检索提醒器。
        /// </summary>
        private Task ReadAndUpdateReminders()
        {
            CheckRuntimeContext();

            if (StoppedCancellationTokenSource.IsCancellationRequested) return Task.CompletedTask;

            var tasks = new List<Task>();
            RemoveOutOfRangeReminders(tasks);

            // 刷新使用偶数序列值。本地写入使用紧随其后的奇数值，因此可以覆盖
            // 此快照而不推进刷新世代。较新的刷新会将序列值增加 2，并使
            // 所有较旧的刷新结果被丢弃。
            var cachedSequence = localTableSequence += 2;
            var rangeSerialNumberCopy = RangeSerialNumber;
            LogTraceRingRange(RingRange, RangeSerialNumber, localReminders.Count);
            foreach (var range in RangeFactory.GetSubRanges(RingRange))
            {
                tasks.Add(ReadAndReconcileRange(range, rangeSerialNumberCopy, cachedSequence));
            }
            var task = Task.WhenAll(tasks);
            if (logger.IsEnabled(LogLevel.Trace)) task.ContinueWith(_ => PrintReminders(), TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);
            return task;
        }

        internal Task<Task> TestOnlyStartRefresh() => QueueRefresh();

        internal Task TestOnlyRefresh() => QueueRefresh().Unwrap();

        internal bool TestOnlyIsStarted => Status == GrainServiceStatus.Started;

        private Task<Task> QueueRefresh()
        {
            try
            {
                return Task.FromResult(QueueTrackedReconciliation(ReadAndUpdateReminders));
            }
            catch (Exception exception)
            {
                return Task.FromException<Task>(exception);
            }
        }

        internal Task TestOnlyWaitForSiloStatusListeners(CancellationToken cancellationToken)
            => _siloStatusListenerManager.TestOnlyWaitForCurrentMembershipVersion(cancellationToken);

        internal (MembershipVersion Current, MembershipVersion Processed) TestOnlyGetMembershipVersions()
            => _siloStatusListenerManager.TestOnlyGetMembershipVersions();

        internal string TestOnlyDescribeTopologyState()
        {
            lock (_reconciliationLock)
            {
                return $"silo={Silo}, serviceStatus={Status}, {_siloStatusListenerManager.TestOnlyDescribeMembershipState()}, "
                    + $"ringRange={RingRange}, reconciliationGeneration={reconciliationGeneration}, "
                    + $"reconciliationCompleted={reconciliationTask.IsCompleted}";
            }
        }

        private void RemoveOutOfRangeReminders(List<Task> removedReminderTasks)
        {
            CheckRuntimeContext();

            var remindersOutOfRange = 0;

            foreach (var r in localReminders)
            {
                if (RingRange.InRange(r.Key.GrainId)) continue;
                remindersOutOfRange++;

                LogTraceRemovingReminder(r.Value);

                // 在本地移除。
                removedReminderTasks.Add(r.Value.StopAsync(ReminderEvents.LocalReminderStopReason.RemovedFromRange));
                localReminders.Remove(r.Key);
            }

            if (remindersOutOfRange > 0)
            {
                LogInfoRemovedLocalReminders(remindersOutOfRange);
            }
        }

        public override Task OnRangeChange(IRingRange oldRange, IRingRange newRange, bool increased)
            => TrackReconciliation(() => ApplyRangeChange(oldRange, newRange, increased));

        void IGrainServiceRangeChangeQueue.QueueRangeChange(IRingRange oldRange, IRingRange newRange, bool increased)
            => QueueTrackedRangeChange(oldRange, newRange, increased).Ignore();

        private Task QueueTrackedRangeChange(IRingRange oldRange, IRingRange newRange, bool increased)
            => QueueTrackedReconciliation(() => ApplyRangeChange(oldRange, newRange, increased));

        private Task QueueTrackedReconciliation(Func<Task> reconciliation)
            => TrackReconciliation(() => this.QueueTask(reconciliation));

        private Task ApplyRangeChange(IRingRange oldRange, IRingRange newRange, bool increased)
        {
            CheckRuntimeContext();

            _ = base.OnRangeChange(oldRange, newRange, increased);
            var status = Status;
            if (status == GrainServiceStatus.Started)
            {
                return ReadAndUpdateReminders();
            }

            LogIgnoringRangeChange(status);
            return Task.CompletedTask;
        }

        private Task TrackReconciliation(Func<Task> reconciliation)
        {
            var reconciliationTaskSource = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource? previousGenerationChanged = null;
            try
            {
                lock (_reconciliationLock)
                {
                    previousGenerationChanged = reconciliationGenerationChanged;
                    reconciliationGenerationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    reconciliationGeneration++;
                    reconciliationTask = reconciliationTaskSource.Task.Unwrap();

                    try
                    {
                        var task = reconciliation();
                        reconciliationTaskSource.SetResult(task);
                        return task;
                    }
                    catch (Exception exception)
                    {
                        reconciliationTaskSource.SetException(exception);
                        throw;
                    }
                }
            }
            finally
            {
                previousGenerationChanged?.TrySetResult();
            }
        }

        internal async Task TestOnlyWaitForRangeChangeReconciliation(CancellationToken cancellationToken)
        {
            while (true)
            {
                // 较新且受跟踪的刷新通过 localTableSequence 取代较旧的结果。跟随
                // 对账世代，确保停滞的过时读取不会阻塞当前读取。
                long observedGeneration;
                Task observedTask;
                Task observedGenerationChanged;
                lock (_reconciliationLock)
                {
                    observedGeneration = reconciliationGeneration;
                    observedTask = reconciliationTask;
                    observedGenerationChanged = reconciliationGenerationChanged.Task;
                }

                await Task.WhenAny(observedTask, observedGenerationChanged).WaitAsync(cancellationToken);

                lock (_reconciliationLock)
                {
                    if (observedGeneration != reconciliationGeneration)
                    {
                        continue;
                    }
                }

                // 世代变更任务只能在世代推进后完成，因此
                // 被观察的对账操作已完成，可以无阻塞地读取其结果。
                await observedTask;
                return;
            }
        }

        internal Task TestOnlyChangeRange(IRingRange oldRange, IRingRange newRange, bool increased)
            => QueueTrackedRangeChange(oldRange, newRange, increased);

        private async Task RunAsync()
        {
            var initialRefreshStagger = reminderOptions.RefreshReminderListPeriod < InitialReadRetryPeriod
                ? reminderOptions.RefreshReminderListPeriod
                : InitialReadRetryPeriod;
            TimeSpan? overrideDelay = RandomTimeSpan.Next(initialRefreshStagger);
            int consecutiveFailures = 0;
            while (await listRefreshTimer.NextTick(overrideDelay))
            {
                try
                {
                    overrideDelay = null;
                    await QueueTrackedReconciliation(ProcessRefreshTick);

                    consecutiveFailures = 0;
                }
                catch (Exception exception)
                {
                    LogWarningReadingReminders(exception);

                    overrideDelay = BackoffComputation.ComputeBackoffDelay(
                        ++consecutiveFailures,
                        baseMin: TimeSpan.FromSeconds(10),
                        baseMax: TimeSpan.FromSeconds(20),
                        cap: TimeSpan.FromSeconds(80));
                }
            }

            async Task ProcessRefreshTick()
            {
                CheckRuntimeContext();

                switch (Status)
                {
                    case GrainServiceStatus.Booting:
                        await DoInitialReadAndUpdateReminders();
                        break;
                    case GrainServiceStatus.Started:
                        await ReadAndUpdateReminders();
                        break;
                    default:
                        listRefreshTimer.Dispose();
                        break;
                }
            }
        }

        protected override async Task StartInBackground()
        {
            CheckRuntimeContext();

            // 加载前观察哈希环，使就绪状态覆盖当前负责范围，包括读取期间的范围变化。
            SubscribeToRangeChangeEvents();
            await this.QueueAction(static _ => { }, state: 0);
            await DoInitialReadAndUpdateReminders();
            using var suppressExecutionContext = new ExecutionContextSuppressor();
            this.runTask = Task.Run(RunAsync);
        }

        private async Task DoInitialReadAndUpdateReminders()
        {
            CheckRuntimeContext();

            try
            {
                if (StoppedCancellationTokenSource.IsCancellationRequested) return;

                initialReadCallCount++;
                while (true)
                {
                    var rangeSerialNumber = RangeSerialNumber;
                    await this.ReadAndUpdateReminders();
                    await this.QueueAction(static _ => { }, state: 0);
                    if (rangeSerialNumber == RangeSerialNumber)
                    {
                        break;
                    }
                }

                Status = GrainServiceStatus.Started;
                startedTask.TrySetResult(true);
                ReminderEvents.EmitReminderServiceStarted(Silo);
            }
            catch (Exception ex)
            {
                if (StoppedCancellationTokenSource.IsCancellationRequested) return;

                if (initialReadCallCount <= InitialReadRetryCountBeforeFastFailForUpdates)
                {
                    LogWarningInitialLoadFailing(ex, initialReadCallCount);
                }
                else
                {
                    LogErrorInitialLoadFailed(ex, initialReadCallCount);
                    startedTask.TrySetException(new OrleansException("ReminderService failed initial load of reminders and cannot guarantee that the service will be eventually start without manual intervention or restarting the silo.", ex));
                }
            }
        }

        private async Task ReadAndReconcileRange(ISingleRange range, int rangeSerialNumberCopy, long cachedSequence)
        {
            CheckRuntimeContext();

            LogDebugReadingRows(range);

            try
            {
                // 读取序列值在任何范围读取让出执行之前就已捕获。在
                // 存储读取期间执行的本地变更会取得更大的序列值，因此在此快照返回时优先采用本地变更。
                ReminderTableData? table = await reminderTable.ReadRows(
                    range.Begin,
                    range.End,
                    StoppedCancellationTokenSource.Token); // 获取所有提醒器，包括本地已有的提醒器。

                if (cachedSequence < localTableSequence)
                {
                    // 较新的刷新已开始，因此此结果不再有权清理删除标记或更改调度。
                    return;
                }

                if (rangeSerialNumberCopy < RangeSerialNumber)
                {
                    LogDebugRangeChangedWhileFromTable(RangeSerialNumber, rangeSerialNumberCopy);
                    return;
                }

                if (StoppedCancellationTokenSource.IsCancellationRequested) return;

                // 针对较旧 Orleans 版本生成的提供程序仍可能返回 null。
                if (table is null) return;

                // 首先收集当前范围中所有已加载的提醒器标识，然后随存储返回的记录逐个移除对应标识。
                // 最终剩余的提醒器已从存储中删除，也必须在本地移除。
                var remindersNotInTable = new HashSet<ReminderIdentity>();
                foreach (var key in localReminders.Keys)
                {
                    if (range.InRange(key.GrainId))
                    {
                        remindersNotInTable.Add(key);
                    }
                }

                LogDebugReadRemindersFromTable(range, table.Reminders.Count, localTableSequence, cachedSequence);
                var tasks = new List<Task>();
                // 整个快照使用同一个时间戳，使每行均根据相同的加载时间窗口进行评估。
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                foreach (var entry in table.Reminders)
                {
                    var key = new ReminderIdentity(entry.GrainId, entry.ReminderName);
                    remindersNotInTable.Remove(key);
                    ReconcileTableEntry(entry, cachedSequence, now, tasks);
                }

                int remindersCountBeforeRemove = localReminders.Count;

                // 较新的本地更新优先于此快照。否则，存储未
                // 返回的提醒器不再由当前服务负责调度。
                foreach (var key in remindersNotInTable)
                {
                    if (!localReminders.TryGetValue(key, out var reminder))
                    {
                        continue;
                    }

                    if (cachedSequence <= reminder.LocalSequenceNumber)
                    {
                        LogTraceNotInTableInLocalNewer(reminder);
                    }
                    else
                    {
                        LogTraceNotInTableInLocalOld(reminder);
                        tasks.Add(
                            RemoveLocalReminder(
                                key,
                                reminder,
                                ReminderEvents.LocalReminderStopReason.RemovedFromTable,
                                cachedSequence));
                    }
                }

                LogDebugRemovedRemindersFromLocalTable(remindersCountBeforeRemove - localReminders.Count);
                await Task.WhenAll(tasks);
            }
            catch (Exception exc)
            {
                LogErrorFailedToReadTableAndStartTimer(exc);
                throw;
            }
        }

        private void ReconcileTableEntry(ReminderEntry entry, long tableSequence, DateTime now, List<Task> stopTasks)
        {
            var key = new ReminderIdentity(entry.GrainId, entry.ReminderName);
            var nextTick = CalculateNextTickTime(entry, now);
            var isWithinLoadingWindow = nextTick <= now.AddClamped(reminderOptions.ReminderLoadingWindow);
            if (!localReminders.TryGetValue(key, out var localReminder))
            {
                // 距下次触发较远的提醒器仅保留在存储中，直到后续刷新使其下次触发时间
                // 进入加载时间窗口。
                if (isWithinLoadingWindow)
                {
                    LogTraceInTableNotInLocal(entry);
                    AddOrUpdateLocalReminder(entry, tableSequence);
                }

                return;
            }

            var state = localReminder.State;
            // 如果直接注册或更新在此次读取开始后才完成，则它比提醒器表
            // 快照更新，因此保持其本地调度不变。
            if (tableSequence <= localReminder.LocalSequenceNumber)
            {
                if (state is LocalReminderState.Running)
                {
                    LogTraceInTableInLocalNewerTicking(localReminder);
                }
                else
                {
                    LogTraceInTableInLocalNewerNotTicking(localReminder);
                }

                return;
            }

            // 如果当前负责方已在同一时间戳触发过该未改变的触发项，则保留对应标记。
            if (state is LocalReminderState.Tombstone
                && localReminder.ShouldRetainFiredOccurrenceTombstone(entry, nextTick))
            {
                localReminder.LocalSequenceNumber = tableSequence;
                return;
            }

            if (!isWithinLoadingWindow)
            {
                LogTraceRemovingReminder(localReminder);
                stopTasks.Add(
                    RemoveLocalReminder(
                        key,
                        localReminder,
                        ReminderEvents.LocalReminderStopReason.OutsideLoadingWindow,
                        tableSequence));
                return;
            }

            if (state is LocalReminderState.Tombstone)
            {
                LogTraceInTableInLocalOldNotTicking(localReminder);
                AddOrUpdateLocalReminder(entry, tableSequence);
                return;
            }

            if (state is not LocalReminderState.Running)
            {
                LogTraceInTableInLocalOldNotTicking(localReminder);
                return;
            }

            LogTraceInTableInLocalOldTicking(localReminder);
            if (!StringComparer.Ordinal.Equals(localReminder.Entry.ETag, entry.ETag))
            {
                LogTraceLocalReminderNeedsUpdate(localReminder);
                AddOrUpdateLocalReminder(entry, tableSequence);
            }
        }

        private void ReconcileLocalReminder(ReminderEntry entry, DateTime now)
        {
            if (IsReminderWithinLoadingWindow(entry, now, reminderOptions.ReminderLoadingWindow))
            {
                AddOrUpdateLocalReminder(entry);
            }
            else
            {
                // 更新后的调度已持久化，但距离下次触发过远，无需保留
                // 本地任务。保留一个已停止且携带序列值的条目，直到较新的刷新观察到此次写入。
                AddLocalReminderTombstone(entry, ReminderEvents.LocalReminderStopReason.OutsideLoadingWindow);
            }
        }

        private void AddOrUpdateLocalReminder(ReminderEntry entry)
            => AddOrUpdateLocalReminder(entry, GetLocalMutationSequence());

        private void AddOrUpdateLocalReminder(ReminderEntry entry, long sequence)
        {
            CheckRuntimeContext();

            var key = new ReminderIdentity(entry.GrainId, entry.ReminderName);
            LocalReminderData reminderData;
            if (localReminders.TryGetValue(key, out var existing))
            {
                if (existing.State is LocalReminderState.Tombstone)
                {
                    reminderData = LocalReminderData.CreateRunnable(entry, this, sequence);
                    localReminders[key] = reminderData;
                    LogDebugStartedReminder(entry);
                }
                else
                {
                    reminderData = existing;
                    reminderData.LocalSequenceNumber = sequence;
                    reminderData.Update(entry);
                    LogDebugUpdatedReminder(entry);
                }
            }
            else
            {
                reminderData = LocalReminderData.CreateRunnable(entry, this, sequence);
                localReminders.Add(key, reminderData);

                LogDebugStartedReminder(entry);
            }

            lock (_deliveryLock)
            {
                if (!_isDeliveringReminders)
                {
                    return;
                }
            }

            reminderData.TryStart();
        }

        private void AddLocalReminderTombstone(ReminderEntry entry, ReminderEvents.LocalReminderStopReason reason)
        {
            var key = new ReminderIdentity(entry.GrainId, entry.ReminderName);
            if (localReminders.TryGetValue(key, out var existing) && existing.State is not LocalReminderState.Tombstone)
            {
                existing.Update(entry);
                RequestLocalReminderRemoval(key, existing, reason);
                return;
            }

            localReminders[key] = LocalReminderData.CreateTombstone(entry, this, reason, GetLocalMutationSequence());
            LogDebugStoppingReminder(entry, reason);
        }

        private void AddLocalReminderTombstone(ReminderIdentity key, ReminderEvents.LocalReminderStopReason reason)
        {
            var sequence = GetLocalMutationSequence();
            localReminders[key] = LocalReminderData.CreateTombstone(key, this, reason, sequence);
        }

        private void RequestLocalReminderRemoval(
            ReminderIdentity key,
            LocalReminderData reminder,
            ReminderEvents.LocalReminderStopReason reason)
        {
            if (!localReminders.TryGetValue(key, out var current) || !ReferenceEquals(current, reminder))
            {
                return;
            }

            var stopTask = reminder.StopAsync(reason, GetLocalMutationSequence());
            ObserveLocalReminderStop(stopTask, key.GrainId, key.ReminderName);
        }

        private long GetLocalMutationSequence() => localTableSequence + 1;

        private Task RemoveLocalReminder(
            ReminderIdentity key,
            LocalReminderData reminder,
            ReminderEvents.LocalReminderStopReason reason,
            long sequence)
        {
            if (!localReminders.TryGetValue(key, out var current) || !ReferenceEquals(current, reminder))
            {
                return Task.CompletedTask;
            }

            var stopTask = reminder.StopAsync(reason, sequence);
            localReminders.Remove(key);
            return stopTask;
        }

        private AdmissionGate.Admission TryBeginSingleReminderDelivery()
        {
            lock (_deliveryLock)
            {
                return _deliveryGate?.TryEnter() ?? default;
            }
        }

        private Task DoResponsibilitySanityCheck(GrainId grainId, string debugInfo)
        {
            CheckRuntimeContext();

            switch (Status)
            {
                case GrainServiceStatus.Booting:
                    // 如果服务尚未完成初次加载，则可能仍在正常加载，或
                    // 已有几次尝试失败，不应让调用方继续等待其完成。
                    var task = this.startedTask.Task;
                    if (task.IsCompleted)
                    {
                        // 检查负责范围之前，先传播初次加载的失败异常（如果有）。
                        task.GetAwaiter().GetResult();
                        CheckRange();
                    }
                    else
                    {
                        return WaitForInitCompletion();
                        async Task WaitForInitCompletion()
                        {
                            try
                            {
                                // 等待初次加载任务完成（带超时限制）。
                                await task.WaitAsync(InitialReadMaxWaitTimeForUpdates);
                            }
                            catch (TimeoutException ex)
                            {
                                throw new OrleansException("Reminder Service is still initializing and it is taking a long time. Please retry again later.", ex);
                            }

                            CheckRange();
                        }
                    }
                    break;
                case GrainServiceStatus.Started:
                    CheckRange();
                    break;
                case GrainServiceStatus.Stopped:
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException("status");
            }

            return Task.CompletedTask;

            void CheckRange()
            {
                CheckRuntimeContext();

                if (!RingRange.InRange(grainId))
                {
                    LogWarningNotResponsible(debugInfo, grainId, RingRange);
                    // 目前仍允许调用方继续，不抛出异常；定期刷新机制将处理提醒器注册到错误 Silo 的情况。
                    // 否则可以拒绝请求，或重新路由请求。
                }
            }
        }

        // 注意：生产环境中的提醒器列表可能非常大！
        private void PrintReminders(string? msg = null)
        {
            if (!logger.IsEnabled(LogLevel.Trace)) return;

            var str = $"{(msg ?? "Current list of reminders:")}{Environment.NewLine}{Utils.EnumerableToString(localReminders, null, Environment.NewLine)}";
            LogTraceReminders(str);
        }

        private IRemindable GetGrain(GrainId grainId) => (IRemindable)_referenceActivator.CreateReference(grainId, _grainInterfaceType);

        internal static DateTime CalculateNextTickTime(ReminderEntry entry, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(entry);
            Debug.Assert(now.Kind == DateTimeKind.Utc);
            if (entry.Period <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Period, "Reminder period must be greater than zero.");
            }

            // 即使存储提供程序丢失 DateTimeKind，提醒器时间戳也表示 UTC 时间。
            var startAt = DateTime.SpecifyKind(entry.StartAt, DateTimeKind.Utc);
            if (now <= startAt)
            {
                return startAt;
            }

            var sinceFirstTick = now.Ticks - startAt.Ticks;
            var sinceLastTick = sinceFirstTick % entry.Period.Ticks;
            if (sinceLastTick == 0)
            {
                return now;
            }

            return now.AddClamped(TimeSpan.FromTicks(entry.Period.Ticks - sinceLastTick));
        }

        internal static bool IsReminderWithinLoadingWindow(ReminderEntry entry, DateTime now, TimeSpan loadingWindow)
        {
            Debug.Assert(now.Kind == DateTimeKind.Utc);
            if (loadingWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(loadingWindow), loadingWindow, "The reminder loading window must be greater than zero.");
            }

            return CalculateNextTickTime(entry, now) <= now.AddClamped(loadingWindow);
        }

        internal static bool ShouldRetainFiredOccurrenceTombstone(
            ReminderEntry localEntry,
            ReminderEntry tableEntry,
            DateTime? lastFiredTickTime,
            DateTime nextTick)
            => lastFiredTickTime == nextTick && StringComparer.Ordinal.Equals(localEntry.ETag, tableEntry.ETag);

        internal static DateTime CalculateFollowingTickTime(ReminderEntry entry, DateTime previousTickTime, DateTime now)
        {
            Debug.Assert(previousTickTime.Kind == DateTimeKind.Utc);
            Debug.Assert(now.Kind == DateTimeKind.Utc);
            var nextTick = CalculateNextTickTime(entry, now);
            return nextTick > previousTickTime ? nextTick : previousTickTime.AddClamped(entry.Period);
        }

        private bool TryEvictLocalReminder(LocalReminderData reminder, long scheduleVersion, DateTime? lastFiredTickTime)
        {
            CheckRuntimeContext();

            var entry = reminder.Entry;
            var key = new ReminderIdentity(entry.GrainId, entry.ReminderName);
            // 只有作出移除决定的本地实例及其调度版本才能执行自身移除。
            // 并发更新会保留提醒器，以便其运行循环观察到新的调度。
            if (!localReminders.TryGetValue(key, out var current) || !ReferenceEquals(current, reminder))
            {
                return true;
            }

            var sequence = GetLocalMutationSequence();
            if (!reminder.TryStopOutsideLoadingWindow(scheduleVersion, sequence, lastFiredTickTime))
            {
                return false;
            }

            // 将已停止的对象保留为删除标记，直到较新的表读取观察到此调度决定。
            return true;
        }

        private enum LocalReminderState
        {
            Stopped,
            Running,
            Tombstone,
        }

        private sealed class LocalReminderData
        {
            private readonly LocalReminderService _shared;
            private readonly CancellationTokenSource _stopCancellation = new();
            private readonly System.Threading.Lock _lock = new();
            private readonly ReminderIdentity _identity;
            private ReminderEntry? _entry;
            private DateTime? _lastFiredTickTime;
            private CancellationTokenSource _scheduleChangedCancellation = new();
            private long _scheduleVersion;

            private int _stopReason;
            private long _localSequenceNumber;
            private Task? _runTask;

            private LocalReminderData(
                ReminderIdentity identity,
                ReminderEntry? entry,
                LocalReminderService reminderService,
                ReminderEvents.LocalReminderStopReason reason,
                long localSequenceNumber)
            {
                _shared = reminderService;
                _entry = entry;
                _identity = identity;
                _localSequenceNumber = localSequenceNumber;
                _stopReason = (int)reason;
            }

            public static LocalReminderData CreateRunnable(
                ReminderEntry entry,
                LocalReminderService reminderService,
                long localSequenceNumber)
                => new(
                    new ReminderIdentity(entry.GrainId, entry.ReminderName),
                    entry,
                    reminderService,
                    ReminderEvents.LocalReminderStopReason.Unknown,
                    localSequenceNumber);

            public static LocalReminderData CreateTombstone(
                ReminderEntry entry,
                LocalReminderService reminderService,
                ReminderEvents.LocalReminderStopReason reason,
                long localSequenceNumber)
                => new(
                    new ReminderIdentity(entry.GrainId, entry.ReminderName),
                    entry,
                    reminderService,
                    reason,
                    localSequenceNumber);

            public static LocalReminderData CreateTombstone(
                ReminderIdentity identity,
                LocalReminderService reminderService,
                ReminderEvents.LocalReminderStopReason reason,
                long localSequenceNumber)
                => new(identity, entry: null, reminderService, reason, localSequenceNumber);

            public ReminderEntry Entry
            {
                get
                {
                    lock (_lock)
                    {
                        return _entry ?? throw new InvalidOperationException($"Reminder {_identity} is a tombstone.");
                    }
                }
            }

            /// <summary>
            /// 在本地使用此值解决定期读取提醒器表与并发本地注册、注销请求之间的竞态。
            /// </summary>
            public long LocalSequenceNumber
            {
                get
                {
                    lock (_lock)
                    {
                        return _localSequenceNumber;
                    }
                }
                set
                {
                    lock (_lock)
                    {
                        _localSequenceNumber = value;
                    }
                }
            }

            public LocalReminderState State
            {
                get
                {
                    lock (_lock)
                    {
                        if (_stopReason != (int)ReminderEvents.LocalReminderStopReason.Unknown)
                        {
                            return LocalReminderState.Tombstone;
                        }

                        return _runTask is Task task && !task.IsCompleted
                            ? LocalReminderState.Running
                            : LocalReminderState.Stopped;
                    }
                }
            }

            public bool ShouldRetainFiredOccurrenceTombstone(ReminderEntry entry, DateTime nextTick)
            {
                lock (_lock)
                {
                    return _entry is { } localEntry
                        && LocalReminderService.ShouldRetainFiredOccurrenceTombstone(
                            localEntry,
                            entry,
                            _lastFiredTickTime,
                            nextTick);
                }
            }

            public bool TryStart()
            {
                GrainId grainId;
                string reminderName;
                lock (_lock)
                {
                    if (_runTask is not null || _stopReason is not (int)ReminderEvents.LocalReminderStopReason.Unknown)
                    {
                        return false;
                    }

                    var entry = _entry ?? throw new InvalidOperationException($"Reminder {_identity} is a tombstone.");
                    grainId = entry.GrainId;
                    reminderName = entry.ReminderName;
                    using var suppressExecutionContext = new ExecutionContextSuppressor();
                    _runTask = RunAsync(grainId, reminderName);
                }

                ReminderEvents.EmitLocalReminderStarted(grainId, reminderName, this, _shared.Silo);
                return true;
            }

            public void Update(ReminderEntry entry)
            {
                ArgumentNullException.ThrowIfNull(entry);

                long scheduleVersion;
                CancellationTokenSource scheduleChangedCancellation;
                lock (_lock)
                {
                    if (_identity.GrainId != entry.GrainId || !StringComparer.Ordinal.Equals(_identity.ReminderName, entry.ReminderName))
                    {
                        throw new InvalidOperationException($"Cannot update reminder {_identity} with {entry} because the reminder identity changed.");
                    }

                    _entry = entry;
                    scheduleVersion = ++_scheduleVersion;
                    scheduleChangedCancellation = _scheduleChangedCancellation;
                    _scheduleChangedCancellation = new();
                }

                ReminderEvents.EmitLocalReminderScheduleChanged(entry.GrainId, entry.ReminderName, this, scheduleVersion, _shared.Silo);
                scheduleChangedCancellation.Cancel();
                scheduleChangedCancellation.Dispose();
            }

            public Task StopAsync(ReminderEvents.LocalReminderStopReason reason, long? localSequenceNumber = null)
            {
                ReminderEntry? entry;
                CancellationTokenSource scheduleChangedCancellation;
                Task? runTask;
                lock (_lock)
                {
                    entry = _entry;
                    if (localSequenceNumber is { } sequence)
                    {
                        _localSequenceNumber = sequence;
                    }

                    if (_stopReason == (int)ReminderEvents.LocalReminderStopReason.Unknown)
                    {
                        _stopReason = (int)reason;
                    }

                    scheduleChangedCancellation = _scheduleChangedCancellation;
                    runTask = _runTask;
                }

                if (entry is not null)
                {
                    _shared.LogDebugStoppingReminder(entry, reason);
                }
                _stopCancellation.Cancel();
                scheduleChangedCancellation.Cancel();
                return runTask ?? Task.CompletedTask;
            }

            public bool TryStopOutsideLoadingWindow(
                long scheduleVersion,
                long localSequenceNumber,
                DateTime? lastFiredTickTime)
            {
                ReminderEntry entry;
                CancellationTokenSource scheduleChangedCancellation;
                lock (_lock)
                {
                    if (_stopReason != (int)ReminderEvents.LocalReminderStopReason.Unknown || _scheduleVersion != scheduleVersion)
                    {
                        return false;
                    }

                    _localSequenceNumber = localSequenceNumber;
                    _lastFiredTickTime = lastFiredTickTime;
                    _stopReason = (int)ReminderEvents.LocalReminderStopReason.OutsideLoadingWindow;
                    entry = _entry ?? throw new InvalidOperationException($"Reminder {_identity} is a tombstone.");
                    scheduleChangedCancellation = _scheduleChangedCancellation;
                }

                _shared.LogDebugStoppingReminder(entry, ReminderEvents.LocalReminderStopReason.OutsideLoadingWindow);
                _stopCancellation.Cancel();
                scheduleChangedCancellation.Cancel();
                return true;
            }

            private async Task RunAsync(GrainId grainId, string reminderName)
            {
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);

                DateTime? previousTickTime = null;
                long previousScheduleVersion = -1;
                try
                {
                    while (await WaitForNextTick(previousTickTime, previousScheduleVersion) is { } scheduledTick)
                    {
                        var entry = PrepareTick(scheduledTick.ScheduleVersion);
                        if (entry is null)
                        {
                            continue;
                        }

                        using var admission = _shared.TryBeginSingleReminderDelivery();
                        if (!admission.Entered)
                        {
                            continue;
                        }

                        try
                        {
                            var before = _shared._timeProvider.GetUtcNow().UtcDateTime;
                            var status = new TickStatus(entry.StartAt, entry.Period, before);

                            LogTraceTriggeringTick(_shared.logger, this, status, before);
                            ReminderEvents.EmitTickFiring(entry.GrainId, entry.ReminderName, status, _shared.Silo);
                            if (_shared._reminderInstruments.TardinessSecondsEnabled)
                            {
                                var tardiness = CalculateTardiness(status);
                                _shared._reminderInstruments.OnTardiness(tardiness);
                            }

                            try
                            {
                                var grainRef = _shared.GetGrain(entry.GrainId);
                                await grainRef.ReceiveReminder(entry.ReminderName, status, _shared.StoppedCancellationTokenSource.Token);

                                if (_shared.logger.IsEnabled(LogLevel.Trace))
                                {
                                    var after = _shared._timeProvider.GetUtcNow().UtcDateTime;
                                    var elapsed = after - before;
                                    var nextTick = CalculateFollowingTickTime(entry, scheduledTick.TickTime, after);
                                    LogTraceTickTriggered(_shared.logger, this, elapsed.TotalSeconds, nextTick);
                                }

                                ReminderEvents.EmitTickCompleted(entry.GrainId, entry.ReminderName, status, _shared.Silo);
                                _shared._reminderInstruments.OnTickDelivered();
                            }
                            catch (Exception exc)
                            {
                                var after = _shared._timeProvider.GetUtcNow().UtcDateTime;
                                var nextTick = CalculateFollowingTickTime(entry, scheduledTick.TickTime, after);
                                LogErrorDeliveringReminderTick(_shared.logger, this, nextTick, exc);
                                ReminderEvents.EmitTickFailed(entry.GrainId, entry.ReminderName, status, exc, _shared.Silo);

                                // 如何处理提醒器触发持续投递失败的情况？
                            }
                        }
                        catch (Exception exception)
                        {
                            LogWarningFiringReminder(_shared.logger, entry.ReminderName, entry.GrainId, exception);
                        }
                        previousTickTime = scheduledTick.TickTime;
                        previousScheduleVersion = scheduledTick.ScheduleVersion;
                    }
                }
                finally
                {
                    ReminderEvents.EmitLocalReminderStopped(
                        grainId,
                        reminderName,
                        this,
                        (ReminderEvents.LocalReminderStopReason)_stopReason,
                        _shared.Silo);
                }
            }

            private async Task<ScheduledTick?> WaitForNextTick(DateTime? previousTickTime, long previousScheduleVersion)
            {
                while (true)
                {
                    ReminderEntry entry;
                    CancellationToken scheduleChangedToken;
                    GrainId grainId;
                    string reminderName;
                    long scheduleVersion;
                    lock (_lock)
                    {
                        if (_stopReason != (int)ReminderEvents.LocalReminderStopReason.Unknown)
                        {
                            return null;
                        }

                        entry = _entry ?? throw new InvalidOperationException($"Reminder {_identity} is a tombstone.");
                        scheduleChangedToken = _scheduleChangedCancellation.Token;
                        grainId = entry.GrainId;
                        reminderName = entry.ReminderName;
                        scheduleVersion = _scheduleVersion;
                    }

                    var now = _shared._timeProvider.GetUtcNow().UtcDateTime;
                    // 将每次触发都锚定在持久化的 StartAt + N * Period 周期上。如果投递耗时过长，
                    // 则跳过错过的触发，而不是根据投递完成时间改变调度节奏。
                    var tickTime = previousTickTime is { } previous && previousScheduleVersion == scheduleVersion
                        ? CalculateFollowingTickTime(entry, previous, now)
                        : CalculateNextTickTime(entry, now);

                    // 距下次触发较远时，丢弃本地任务。提醒器仍然持久化，
                    // 当它接近加载时间窗口时，后续的表刷新会重新加载它。
                    if (tickTime > now.AddClamped(_shared.reminderOptions.ReminderLoadingWindow))
                    {
                        if (_shared.TryEvictLocalReminder(this, scheduleVersion, previousTickTime))
                        {
                            return null;
                        }

                        continue;
                    }

                    using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stopCancellation.Token, scheduleChangedToken);
                    try
                    {
                        // 到期时间不晚于当前时刻的触发使用最短的异步延迟，避免紧密循环。
                        var waitUntil = tickTime <= now ? now.AddClamped(MinimumReminderDueTime) : tickTime;
                        // 即使存储提供程序丢失 DateTimeKind，提醒器时间戳也表示 UTC 时间。
                        var delayTask = _shared._timeProvider.DelayUntilAsync(
                            new DateTimeOffset(waitUntil.Ticks, TimeSpan.Zero),
                            waitCancellation.Token);
                        if (!scheduleChangedToken.IsCancellationRequested && !_stopCancellation.IsCancellationRequested)
                        {
                            ReminderEvents.EmitLocalReminderTickWaitArmed(grainId, reminderName, this, scheduleVersion, _shared.Silo);
                        }

                        await delayTask;
                        lock (_lock)
                        {
                            if (_stopReason != (int)ReminderEvents.LocalReminderStopReason.Unknown)
                            {
                                return null;
                            }

                            if (_scheduleVersion != scheduleVersion)
                            {
                                continue;
                            }

                            return new ScheduledTick(scheduleVersion, tickTime);
                        }
                    }
                    catch (OperationCanceledException) when (_stopCancellation.IsCancellationRequested)
                    {
                        return null;
                    }
                    catch (OperationCanceledException) when (scheduleChangedToken.IsCancellationRequested)
                    {
                        continue;
                    }
                }
            }

            private ReminderEntry? PrepareTick(long scheduleVersion)
            {
                lock (_lock)
                {
                    if (_stopReason != (int)ReminderEvents.LocalReminderStopReason.Unknown)
                    {
                        return null;
                    }

                    if (_scheduleVersion != scheduleVersion)
                    {
                        return null;
                    }

                    return _entry;
                }
            }

            private static TimeSpan CalculateTardiness(TickStatus status)
            {
                if (status.Period <= TimeSpan.Zero || status.CurrentTickTime <= status.FirstTickTime)
                {
                    return TimeSpan.Zero;
                }

                var sinceFirstTick = status.CurrentTickTime - status.FirstTickTime;
                return TimeSpan.FromTicks(sinceFirstTick.Ticks % status.Period.Ticks);
            }

            public override string ToString()
            {
                lock (_lock)
                {
                    var isRunning = _runTask is Task task && !task.IsCompleted;
                    return _entry is { } entry
                        ? $"[{entry.ReminderName}, {entry.GrainId}, {entry.Period}, {LogFormatter.PrintDate(entry.StartAt)}, {entry.ETag}, {_localSequenceNumber}, {(isRunning ? "Ticking" : "Stopped")}]"
                        : $"[{_identity.ReminderName}, {_identity.GrainId}, tombstone, {_localSequenceNumber}]";
                }
            }

            private readonly record struct ScheduledTick(long ScheduleVersion, DateTime TickTime);
        }

        private readonly struct ReminderIdentity(GrainId grainId, string reminderName) : IEquatable<ReminderIdentity>
        {
            public readonly GrainId GrainId = grainId;
            public readonly string ReminderName = reminderName;

            public readonly bool Equals(ReminderIdentity other) => GrainId.Equals(other.GrainId) && ReminderName.Equals(other.ReminderName, StringComparison.Ordinal);

            public override readonly bool Equals(object? other) => other is ReminderIdentity id && Equals(id);

            public override readonly int GetHashCode() => HashCode.Combine(GrainId, ReminderName);
        }

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error activating reminder service."
        )]
        private partial void LogErrorActivatingReminderService(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error stopping reminder service."
        )]
        private partial void LogErrorStoppingReminderService(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Error starting reminder service."
        )]
        private partial void LogErrorStartingReminderService(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_RegisterOrUpdate,
            Message = "RegisterOrUpdateReminder: {Entry}"
        )]
        private partial void LogDebugRegisterOrUpdateReminder(ReminderEntry entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Registered reminder {Entry} in table, assigned localSequence {LocalSequence}"
        )]
        private partial void LogDebugRegisterReminder(ReminderEntry entry, long localSequence);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.RS_Register_TableError,
            Message = "Could not register reminder {Entry} to reminder table due to a race. Please try again later."
        )]
        private partial void LogErrorRegisterReminder(ReminderEntry entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_Unregister,
            Message = "UnregisterReminder: {Entry}, LocalTableSequence: {LocalTableSequence}"
        )]
        private partial void LogDebugUnregisterReminder(IGrainReminder entry, long localTableSequence);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_Stop,
            Message = "Requested stop for reminder {Entry}"
        )]
        private partial void LogStoppedReminder(IGrainReminder entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_RemoveFromTable,
            Message = "Removed reminder from table which I didn't have locally: {Entry}."
        )]
        private partial void LogRemovedReminderFromTable(IGrainReminder entry);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Reminder was already absent from the reminder table during unregister: {Entry}."
        )]
        private partial void LogDebugReminderAlreadyRemoved(IGrainReminder entry);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.RS_Unregister_TableError,
            Message = "Could not unregister reminder {Reminder} from the reminder table, due to tag mismatch. You can retry."
        )]
        private partial void LogErrorUnregisterReminder(IGrainReminder reminder);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Local reminder stop failed for GrainId={GrainId}, ReminderName={ReminderName}"
        )]
        private partial void LogErrorStoppingLocalReminder(Exception exception, GrainId grainId, string reminderName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_GetReminder,
            Message = "GetReminder: GrainId={GrainId} ReminderName={ReminderName}"
        )]
        private partial void LogDebugGetReminder(GrainId grainId, string reminderName);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_GetReminders,
            Message = "GetReminders: GrainId={GrainId}"
        )]
        private partial void LogDebugGetReminders(GrainId grainId);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "My range {RingRange}, RangeSerialNumber {RangeSerialNumber}. Local reminders count {LocalRemindersCount}"
        )]
        private partial void LogTraceRingRange(IRingRange ringRange, int rangeSerialNumber, int localRemindersCount);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Not in my range anymore, so removing. {Reminder}"
        )]
        private partial void LogTraceRemovingReminder(LocalReminderData reminder);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Removed {RemovedCount} local reminders that are now out of my range."
        )]
        private partial void LogInfoRemovedLocalReminders(int removedCount);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Ignoring range change until ReminderService is Started -- Current status = {Status}"
        )]
        private partial void LogIgnoringRangeChange(GrainServiceStatus status);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception while reading reminders"
        )]
        private partial void LogWarningReadingReminders(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.RS_ServiceInitialLoadFailing,
            Message = "ReminderService failed initial load of reminders and will retry. Attempt #{AttemptNumber}"
        )]
        private partial void LogWarningInitialLoadFailing(Exception exception, uint attemptNumber);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.RS_ServiceInitialLoadFailed,
            Message = "ReminderService failed initial load of reminders and cannot guarantee that the service will be eventually start without manual intervention or restarting the silo. Attempt #{AttemptNumber}"
        )]
        private partial void LogErrorInitialLoadFailed(Exception exception, uint attemptNumber);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Reading rows from {Range}"
        )]
        private partial void LogDebugReadingRows(IRingRange range);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "My range changed while reading from the table, ignoring the results. Another read has been started. RangeSerialNumber {RangeSerialNumber}, RangeSerialNumberCopy {RangeSerialNumberCopy}."
        )]
        private partial void LogDebugRangeChangedWhileFromTable(int rangeSerialNumber, int rangeSerialNumberCopy);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "For range {Range}, I read in {ReminderCount} reminders from table. LocalTableSequence {LocalTableSequence}, CachedSequence {CachedSequence}"
        )]
        private partial void LogDebugReadRemindersFromTable(IRingRange range, int reminderCount, long localTableSequence, long cachedSequence);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "In table, In local, Old, & Ticking {LocalReminder}"
        )]
        private partial void LogTraceInTableInLocalOldTicking(LocalReminderData localReminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{LocalReminder} needs an in-place update"
        )]
        private partial void LogTraceLocalReminderNeedsUpdate(LocalReminderData localReminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "In table, In local, Old, & Not Ticking {LocalReminder}"
        )]
        private partial void LogTraceInTableInLocalOldNotTicking(LocalReminderData localReminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "In table, In local, Newer, & Ticking {LocalReminder}"
        )]
        private partial void LogTraceInTableInLocalNewerTicking(LocalReminderData localReminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "In table, In local, Newer, & Not Ticking {LocalReminder}"
        )]
        private partial void LogTraceInTableInLocalNewerNotTicking(LocalReminderData localReminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "In table, Not in local, {Reminder}"
        )]
        private partial void LogTraceInTableNotInLocal(ReminderEntry reminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Not in table, In local, Newer, {Reminder}"
        )]
        private partial void LogTraceNotInTableInLocalNewer(LocalReminderData reminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Not in table, In local, Old, so removing. {Reminder}"
        )]
        private partial void LogTraceNotInTableInLocalOld(LocalReminderData reminder);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "{Message}"
        )]
        private partial void LogTraceReminders(string message);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Removed {RemovedCount} reminders from local table"
        )]
        private partial void LogDebugRemovedRemindersFromLocalTable(int removedCount);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.RS_FailedToReadTableAndStartTimer,
            Message = "Failed to read rows from table."
        )]
        private partial void LogErrorFailedToReadTableAndStartTimer(Exception exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_LocalStop,
            Message = "Locally stopping reminder {Reminder} with reason {Reason}"
        )]
        private partial void LogDebugStoppingReminder(ReminderEntry reminder, ReminderEvents.LocalReminderStopReason reason);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_Started,
            Message = "Started reminder {Reminder}."
        )]
        private partial void LogDebugStartedReminder(ReminderEntry reminder);

        [LoggerMessage(
            Level = LogLevel.Debug,
            EventId = (int)ErrorCode.RS_Started,
            Message = "Updated reminder {Reminder} in place."
        )]
        private partial void LogDebugUpdatedReminder(ReminderEntry reminder);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = (int)ErrorCode.RS_NotResponsible,
            Message = "I shouldn't have received request '{Request}' for {GrainId}. It is not in my responsibility range: {Range}"
        )]
        private partial void LogWarningNotResponsible(string request, GrainId grainId, IRingRange range);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Exception firing reminder \"{ReminderName}\" for grain {GrainId}"
        )]
        private static partial void LogWarningFiringReminder(ILogger logger, string reminderName, GrainId grainId, Exception exception);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Triggering tick for {Instance}, status {Status}, now {CurrentTime}"
        )]
        private static partial void LogTraceTriggeringTick(ILogger logger, LocalReminderData instance, TickStatus status, DateTime currentTime);

        private void LogTraceTickTriggeredHelper(LocalReminderData instance, double dueTime, DateTime nextDueTime)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {

            }
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Tick triggered for {Instance}, dt {DueTime} sec, next@~ {NextDueTime}"
        )]
        private static partial void LogTraceTickTriggered(ILogger logger, LocalReminderData instance, double dueTime, DateTime nextDueTime);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not deliver reminder tick for {Instance}, next {NextDueTime}."
        )]
        private static partial void LogErrorDeliveringReminderTick(ILogger logger, LocalReminderData instance, DateTime nextDueTime, Exception exception);
    }
}
