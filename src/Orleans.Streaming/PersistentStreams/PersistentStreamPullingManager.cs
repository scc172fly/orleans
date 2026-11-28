using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Configuration;
using RunState = Orleans.Configuration.StreamLifecycleOptions.RunState;
using Orleans.Internal;
using System.Threading;
using Orleans.Streams.Filtering;
using Orleans.Runtime.Scheduler;
using System.Diagnostics.Metrics;
using StreamingEvents = Orleans.Streaming.Diagnostics.StreamingEvents;

namespace Orleans.Streams
{
    internal sealed partial class PersistentStreamPullingManager : SystemTarget, IPersistentStreamPullingManager, IStreamQueueBalanceListener
    {
        private static readonly TimeSpan QUEUES_PRINT_PERIOD = TimeSpan.FromMinutes(5);

        private readonly Dictionary<QueueId, PersistentStreamPullingAgent> queuesToAgentsMap;
        private readonly Dictionary<QueueId, PersistentStreamPullingAgent> deactivatedAgents = new();
        private readonly string streamProviderName;
        private readonly IStreamPubSub pubSub;
        private readonly SystemTargetShared _systemTargetShared;

        private readonly StreamPullingAgentOptions options;
        private readonly AsyncSerialExecutor nonReentrancyGuarantor; // 用于以不可重入的方式执行队列变化通知。
        private readonly ILogger logger;
        private int latestRingNotificationSequenceNumber;
        private int latestCommandNumber;
        private readonly IQueueAdapter queueAdapter;
        private readonly IBackoffProvider _deliveryBackoffProvider;
        private readonly IBackoffProvider _queueReaderBackoffProvider;
        private readonly IQueueAdapterCache queueAdapterCache;
        private IStreamQueueBalancer queueBalancer;
        private readonly IStreamFilter streamFilter;
        private readonly IQueueAdapterFactory adapterFactory;
        private readonly TimeProvider _timeProvider;
        private readonly StreamInstruments _streamInstruments;
        private RunState managerState;
        private IDisposable? queuePrintTimer;
        private int nextAgentId;
        private int NumberRunningAgents { get { return queuesToAgentsMap.Count; } }


        internal PersistentStreamPullingManager(
            SystemTargetGrainId managerId,
            string strProviderName,
            IStreamPubSub streamPubSub,
            IQueueAdapterFactory adapterFactory,
            IStreamQueueBalancer streamQueueBalancer,
            IStreamFilter streamFilter,
            StreamPullingAgentOptions options,
            IQueueAdapter queueAdapter,
            IBackoffProvider deliveryBackoffProvider,
            IBackoffProvider queueReaderBackoffProvider,
            TimeProvider timeProvider,
            StreamInstruments streamInstruments,
            SystemTargetShared shared)
            : base(managerId, shared)
        {
            if (string.IsNullOrWhiteSpace(strProviderName))
            {
                throw new ArgumentNullException(nameof(strProviderName));
            }

            if (streamPubSub == null)
            {
                throw new ArgumentNullException(nameof(streamPubSub), "StreamPubSub reference should not be null");
            }

            if (streamQueueBalancer == null)
            {
                throw new ArgumentNullException(nameof(streamQueueBalancer), "IStreamQueueBalancer streamQueueBalancer reference should not be null");
            }

            queuesToAgentsMap = new Dictionary<QueueId, PersistentStreamPullingAgent>();
            streamProviderName = strProviderName;
            pubSub = streamPubSub;
            this.options = options;
            nonReentrancyGuarantor = new AsyncSerialExecutor();
            latestRingNotificationSequenceNumber = 0;
            latestCommandNumber = 0;
            queueBalancer = streamQueueBalancer;
            this.streamFilter = streamFilter;
            this.adapterFactory = adapterFactory;
            this.queueAdapter = queueAdapter ?? throw new ArgumentNullException(nameof(queueAdapter));
            _deliveryBackoffProvider = deliveryBackoffProvider;
            _queueReaderBackoffProvider = queueReaderBackoffProvider;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _streamInstruments = streamInstruments;
            _systemTargetShared = shared;
            queueAdapterCache = adapterFactory.GetQueueAdapterCache();
            logger = shared.LoggerFactory.CreateLogger($"{GetType().FullName}.{streamProviderName}");
            LogInfoCreated(GetType().Name, streamProviderName);
            _streamInstruments.RegisterPersistentStreamPullingAgentsObserve(() => new Measurement<int>(queuesToAgentsMap.Count, new KeyValuePair<string, object?>("name", streamProviderName)));
            shared.ActivationDirectory.RecordNewTarget(this);
        }

        public async Task Initialize(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogInfoInit();

            await this.queueBalancer.Initialize(this.adapterFactory.GetStreamQueueMapper());
            queueBalancer.SubscribeToQueueDistributionChangeEvents(this);

            List<QueueId> myQueues = queueBalancer.GetMyQueues().ToList();
            LogInfoInitialized(myQueues.Count, new(myQueues));

            queuePrintTimer = this.RegisterTimer(AsyncTimerCallback, null, QUEUES_PRINT_PERIOD, QUEUES_PRINT_PERIOD);
            managerState = RunState.Initialized;
        }

        public async Task Stop(CancellationToken cancellationToken)
        {
            var balancer = this.queueBalancer;
            if (balancer is null)
            {
                return;
            }

            await StopAgents(cancellationToken);
            if (queuePrintTimer != null)
            {
                queuePrintTimer.Dispose();
                this.queuePrintTimer = null;
            }
            this.queueBalancer = null!;
            await balancer.Shutdown();
        }

        public async Task StartAgents(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            managerState = RunState.AgentsStarted;
            List<QueueId> myQueues = queueBalancer.GetMyQueues().ToList();
            var previousQueues = CaptureAgentQueuesIfDiagnosticsEnabled();

            LogInfoStarting(myQueues.Count, new(myQueues));
            await AddNewQueues(myQueues, true, CancellationToken.None);
            EmitAgentQueueChange(previousQueues);
            LogInfoStarted();
            EmitPullingAgentManagerState();
        }

        public async Task StopAgents(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            managerState = RunState.AgentsStopped;
            List<QueueId> queuesToRemove = queuesToAgentsMap.Keys.ToList();
            LogInfoStopping(queuesToRemove.Count, new(queuesToRemove));
            await RemoveQueues(queuesToRemove, CancellationToken.None);
            EmitAgentQueueChange(queuesToRemove);
            LogInfoStopped();
        }

        /// <summary>
        /// 队列分布因故障或新成员加入而发生变化时应采取的操作。
        /// 由于此拉取管理器是系统目标，且队列分布变化通知
        /// 以 Grain 方法调用的方式投递给它，因此通知不可重入。为简化
        /// 通知处理，我们以不可重入的方式串行执行通知。如果已经收到较新的通知，
        /// 则抑制较早的通知，不执行它。
        /// </summary>
        public Task QueueDistributionChangeNotification()
        {
            return this.RunOrQueueTask(() => this.HandleQueueDistributionChangeNotification());
        }

        public Task HandleQueueDistributionChangeNotification()
        {
            latestRingNotificationSequenceNumber++;
            int notificationSeqNumber = latestRingNotificationSequenceNumber;
            LogInfoGotQueueChangeNotification(notificationSeqNumber, managerState);
            if (managerState == RunState.AgentsStopped)
            {
                return Task.CompletedTask; // 如果代理未运行，则无需在代理之间再均衡队列。
            }

            return nonReentrancyGuarantor.AddNext(() =>
            {
                // 已收到较新的区间更新通知，因此跳过较早或先前的通知。
                if (notificationSeqNumber < latestRingNotificationSequenceNumber)
                {
                    LogInfoSkipQueueChangeNotification(notificationSeqNumber, latestRingNotificationSequenceNumber);
                    return Task.CompletedTask;
                }
                if (managerState == RunState.AgentsStopped)
                {
                    return Task.CompletedTask; // 如果代理未运行，则无需在代理之间再均衡队列。
                }
                return QueueDistributionChangeNotification(notificationSeqNumber);
            });
        }

        private async Task QueueDistributionChangeNotification(int notificationSeqNumber)
        {
            // GetMyQueues 不为 null，因此 ToSet 不会返回 null。
            HashSet<QueueId> currentQueues = queueBalancer.GetMyQueues().ToSet()!;
            IReadOnlyCollection<QueueId>? previousQueues = CaptureAgentQueuesIfDiagnosticsEnabled();
            LogInfoExecutingQueueChangeNotification(
                notificationSeqNumber,
                currentQueues.Count,
                new(currentQueues));

            try
            {
                Task t1 = AddNewQueues(currentQueues, false);

                List<QueueId> queuesToRemove = queuesToAgentsMap.Keys.Where(queueId => !currentQueues.Contains(queueId)).ToList();
                Task t2 = RemoveQueues(queuesToRemove);

                await Task.WhenAll(t1, t2);
            }
            finally
            {
                EmitAgentQueueChange(previousQueues);
                LogInfoDoneExecutingQueueChangeNotification(
                    notificationSeqNumber,
                    NumberRunningAgents,
                    new(queuesToAgentsMap.Keys));
                EmitPullingAgentManagerState();
            }
        }

        private QueueId[]? CaptureAgentQueuesIfDiagnosticsEnabled() => StreamingEvents.IsBalancerChangedEnabled() ? queuesToAgentsMap.Keys.ToArray() : null;

        private void EmitAgentQueueChange(IReadOnlyCollection<QueueId>? previousQueues)
        {
            if (previousQueues is null || !StreamingEvents.IsBalancerChangedEnabled())
            {
                return;
            }

            if (HasSameAgentQueues(previousQueues))
            {
                return;
            }

            StreamingEvents.EmitQueueChange(streamProviderName, Silo, previousQueues as QueueId[] ?? [.. previousQueues], queuesToAgentsMap.Keys.ToArray(), queueBalancer);
        }

        private bool HasSameAgentQueues(IReadOnlyCollection<QueueId> previousQueues)
        {
            if (previousQueues.Count != queuesToAgentsMap.Count)
            {
                return false;
            }

            foreach (var queueId in previousQueues)
            {
                if (!queuesToAgentsMap.ContainsKey(queueId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 接管通过新区间分配给我的一组新队列。
        /// 首先为每个新队列创建一个拉取代理并存入内部数据结构，然后尝试初始化代理。
        /// 错误处理：
        /// 处理初始化和关闭失败的责任由代理代码承担。
        /// 管理器将调用 Initialize 一次并记录错误。它不会再次调用初始化，并将假定初始化已成功。
        /// 关闭操作也是如此。
        /// </summary>
        /// <param name="myQueues"></param>
        /// <param name="failOnInit"></param>
        /// <returns></returns>
        private async Task AddNewQueues(IEnumerable<QueueId> myQueues, bool failOnInit, CancellationToken cancellationToken = default)
        {
            // 为区间内尚未拥有的队列创建代理。
            // 先创建代理并存入本地 queuesToAgentsMap。
            // 完成之后才初始化所有代理。
            var agents = new List<PersistentStreamPullingAgent>();
            foreach (var queueId in myQueues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (queuesToAgentsMap.ContainsKey(queueId))
                {
                    continue;
                }
                else if (deactivatedAgents.Remove(queueId, out var agent))
                {
                    queuesToAgentsMap[queueId] = agent;
                    agents.Add(agent);
                }
                else
                {
                    // 创建新代理。
                    try
                    {
                        var agentIdNumber = Interlocked.Increment(ref nextAgentId);
                        var agentId = SystemTargetGrainId.Create(Constants.StreamPullingAgentType, this.Silo, $"{streamProviderName}_{agentIdNumber}_{queueId:H}");
                        IStreamFailureHandler deliveryFailureHandler = await adapterFactory.GetDeliveryFailureHandler(queueId);
                        agent = new PersistentStreamPullingAgent(
                            agentId,
                            streamProviderName,
                            pubSub,
                            streamFilter,
                            queueId,
                            this.options,
                            queueAdapter,
                            queueAdapterCache,
                            deliveryFailureHandler,
                            _deliveryBackoffProvider,
                            _queueReaderBackoffProvider,
                            _timeProvider,
                            _systemTargetShared,
                            _streamInstruments);
                        queuesToAgentsMap.Add(queueId, agent);
                        agents.Add(agent);
                    }
                    catch (Exception exc)
                    {
                        LogErrorCreatingAgent(exc);
                        // 应如何处理？此错误不可恢复，应视为程序缺陷。但我们不希望因此导致 Silo 停止运行。
                        // 如果此时 Silo 正在启动且代理正在初始化，则使 Silo 启动失败。否则，忽略异常以限制对其他接收器的影响。
                        if (failOnInit) throw;
                    }
                }
            }

            try
            {
                var initTasks = new List<Task>();
                foreach (var agent in agents)
                {
                    initTasks.Add(InitAgent(agent, cancellationToken));
                }
                await Task.WhenAll(initTasks);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 忽略此异常，继续执行，就好像 Initialize 已成功一样。
                // 已记录各次 Initialize 调用的异常，无需再次记录。
            }
            if (agents.Count > 0)
            {
                LogInfoAddedQueues(
                    agents.Count,
                    new(agents),
                    NumberRunningAgents,
                    new(queuesToAgentsMap.Keys));
            }
        }

        private async Task InitAgent(PersistentStreamPullingAgent agent, CancellationToken cancellationToken)
        {
            // 仅在代理完成本地注册后才初始化它。
            var agentGrainRef = agent.AsReference<IPersistentStreamPullingAgent>();

            // 必须通过 Grain 引用调用它。
            try
            {
                await agentGrainRef.Initialize(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exc)
            {
                LogErrorInitializingAgent(agent.QueueId, exc);
            }
        }

        private async Task RemoveQueues(List<QueueId> queuesToRemove, CancellationToken cancellationToken = default)
        {
            if (queuesToRemove.Count == 0)
            {
                return;
            }
            // 停止负责已不在我的区间内的队列的代理。
            LogInfoRemovingAgents(queuesToRemove.Count, new(queuesToRemove));
            var agents = new List<PersistentStreamPullingAgent>(queuesToRemove.Count);
            var removeTasks = new List<Task>();
            foreach (var queueId in queuesToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!queuesToAgentsMap.Remove(queueId, out var agent))
                {
                    continue;
                }

                agents.Add(agent);
                deactivatedAgents[queueId] = agent;
                var task = OrleansTaskExtentions.SafeExecute(() => agent.RunOrQueueTask(() => agent.Shutdown(cancellationToken)));
                task = task.LogException(logger, ErrorCode.PersistentStreamPullingManager_11,
                    $"PersistentStreamPullingAgent {agent.QueueId} failed to Shutdown.");
                removeTasks.Add(task);
            }
            try
            {
                await Task.WhenAll(removeTasks);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 忽略此异常，继续执行，就好像 Initialize 已成功一样。
                // 已记录各次 Shutdown 调用的异常，无需再次记录。
            }

            if (agents.Count > 0)
            {
                LogInfoRemovedQueues(
                    agents.Count,
                    new(agents),
                    NumberRunningAgents,
                    new(queuesToAgentsMap.Keys));
            }
        }

        private void EmitPullingAgentManagerState()
        {
            StreamingEvents.EmitPullingAgentManagerState(streamProviderName, Silo, queuesToAgentsMap.Keys, NumberRunningAgents);
        }

        public async Task<object?> ExecuteCommand(PersistentStreamProviderCommand command, object? arg, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latestCommandNumber++;
            int commandSeqNumber = latestCommandNumber;

            try
            {
                LogInfoGotCommand(
                    command,
                    arg != null ? " with arg " + arg : string.Empty,
                    commandSeqNumber,
                    managerState);

                switch (command)
                {
                    case PersistentStreamProviderCommand.StartAgents:
                    case PersistentStreamProviderCommand.StopAgents:
                        await QueueCommandForExecution(command, commandSeqNumber, cancellationToken);
                        return null;
                    case PersistentStreamProviderCommand.GetAgentsState:
                        return managerState;
                    case PersistentStreamProviderCommand.GetNumberRunningAgents:
                        return NumberRunningAgents;
                    default:
                        throw new OrleansException($"PullingAgentManager does not support command {command}.");
                }
            }
            finally
            {
                LogInfoDoneExecutingCommand(
                    command,
                    commandSeqNumber,
                    managerState,
                    NumberRunningAgents);
            }
        }

        // Start 和 Stop 是需要多个执行轮次的复合命令。
        // 不希望这些命令与其他并发 Start/Stop 命令或 QueueDistributionChangeNotification 交错执行。
        // 因此，通过同一个 nonReentrancyGuarantor 将它们全部串行化。
        private Task QueueCommandForExecution(PersistentStreamProviderCommand command, int commandSeqNumber, CancellationToken cancellationToken)
        {
            return nonReentrancyGuarantor.AddNext(() =>
            {
                // 已收到较新的命令，因此跳过较早或先前的命令。
                if (commandSeqNumber < latestCommandNumber)
                {
                    LogInfoSkipCommandExecution(
                        commandSeqNumber,
                        latestCommandNumber);
                    return Task.CompletedTask;
                }
                switch (command)
                {
                    case PersistentStreamProviderCommand.StartAgents:
                        return StartAgents(cancellationToken);
                    case PersistentStreamProviderCommand.StopAgents:
                        return StopAgents(cancellationToken);
                    default:
                        throw new OrleansException($"PullingAgentManager got unsupported command {command}");
                }
            });
        }

        private static string PrintQueues(ICollection<QueueId> myQueues) => Utils.EnumerableToString(myQueues);

        // 定期输出队列分配信息，便于监控。
        private Task AsyncTimerCallback(object? state)
        {
            LogInfoPeriodicPrint(NumberRunningAgents, streamProviderName, new(queuesToAgentsMap.Keys));
            return Task.CompletedTask;
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_01,
            Message = "Created {Name} for Stream Provider {StreamProvider}."
        )]
        private partial void LogInfoCreated(string name, string streamProvider);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_02,
            Message = "Init."
        )]
        private partial void LogInfoInit();

        private readonly struct QueueIdsLogRecord(ICollection<QueueId> queues)
        {
            public override string ToString() => PrintQueues(queues);
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_03,
            Message = "Initialize: I am now responsible for {QueueCount} queues: {Queues}."
        )]
        private partial void LogInfoInitialized(int queueCount, QueueIdsLogRecord queues);

        //
        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_Starting,
            Message = "Starting agents for {QueueCount} queues: {Queues}."
        )]
        private partial void LogInfoStarting(int queueCount, QueueIdsLogRecord queues);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_Started,
            Message = "Started agents."
        )]
        private partial void LogInfoStarted();

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_Stopping,
            Message = "Stopping agents for {RemovedCount} queues: {RemovedQueues}."
        )]
        private partial void LogInfoStopping(int removedCount, QueueIdsLogRecord removedQueues);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_Stopped,
            Message = "Stopped agents."
        )]
        private partial void LogInfoStopped();

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_04,
            Message = "Got QueueChangeNotification number {NotificationSequenceNumber} from the queue balancer. managerState = {ManagerState}."
        )]
        private partial void LogInfoGotQueueChangeNotification(int notificationSequenceNumber, RunState managerState);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_05,
            Message = "Skipping execution of QueueChangeNotification number {NotificationSequenceNumber} from the queue allocator since already received a later notification (already have notification number {LatestNotificationNumber})."
        )]
        private partial void LogInfoSkipQueueChangeNotification(int notificationSequenceNumber, int latestNotificationNumber);

        //  "Executing QueueChangeNotification number {NotificationSequenceNumber}. Queue balancer says I should now own {QueueCount} queues: {Queues}"
        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_06,
            Message = "Executing QueueChangeNotification number {NotificationSequenceNumber}. Queue balancer says I should now own {QueueCount} queues: {Queues}."
        )]
        private partial void LogInfoExecutingQueueChangeNotification(int notificationSequenceNumber, int queueCount, QueueIdsLogRecord queues);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_16,
            Message = "Done Executing QueueChangeNotification number {NotificationSequenceNumber}. I now own {QueueCount} queues: {Queues}."
        )]
        private partial void LogInfoDoneExecutingQueueChangeNotification(int notificationSequenceNumber, int queueCount, QueueIdsLogRecord queues);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_07,
            Message = "Exception while creating PersistentStreamPullingAgent."
        )]
        private partial void LogErrorCreatingAgent(Exception exc);

        private readonly struct AgentsLogRecord(List<PersistentStreamPullingAgent> agents)
        {
            public override string ToString() => Utils.EnumerableToString(agents, agent => agent.QueueId.ToString());
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_08,
            Message = "Added {AddedCount} new queues: {AddedQueues}. Now own total of {QueueCount} queues: {Queues}."
        )]
        private partial void LogInfoAddedQueues(int addedCount, AgentsLogRecord addedQueues, int queueCount, QueueIdsLogRecord queues);

        [LoggerMessage(
            Level = LogLevel.Error,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_09,
            Message = "PersistentStreamPullingAgent {QueueId} failed to Initialize."
        )]
        private partial void LogErrorInitializingAgent(QueueId queueId, Exception exc);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_10,
            Message = "About to remove {RemovedCount} agents from my responsibility: {RemovedQueues}."
        )]
        private partial void LogInfoRemovingAgents(int removedCount, QueueIdsLogRecord removedQueues);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_10,
            Message = "Removed {RemovedCount} queues: {RemovedQueues}. Now own total of {QueueCount} queues: {Queues}."
        )]
        private partial void LogInfoRemovedQueues(int removedCount, AgentsLogRecord removedQueues, int queueCount, QueueIdsLogRecord queues);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_13,
            Message = "Got command {Command}{ArgString}: commandSeqNumber = {CommandSequenceNumber}, managerState = {ManagerState}."
        )]
        private partial void LogInfoGotCommand(PersistentStreamProviderCommand command, string argString, int commandSequenceNumber, RunState managerState);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_15,
            Message = "Done executing command {Command}: commandSeqNumber = {CommandSequenceNumber}, managerState = {ManagerState}, num running agents = {NumRunningAgents}."
        )]
        private partial void LogInfoDoneExecutingCommand(PersistentStreamProviderCommand command, int commandSequenceNumber, RunState managerState, int numRunningAgents);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_15,
            Message = "Skipping execution of command number {CommandNumber} since already received a later command (already have command number {LatestCommandNumber})."
        )]
        private partial void LogInfoSkipCommandExecution(int commandNumber, int latestCommandNumber);

        [LoggerMessage(
            Level = LogLevel.Information,
            EventId = (int)ErrorCode.PersistentStreamPullingManager_PeriodicPrint,
            Message = "I am responsible for a total of {QueueCount} queues on stream provider {StreamProviderName}: {Queues}."
        )]
        private partial void LogInfoPeriodicPrint(int queueCount, string streamProviderName, QueueIdsLogRecord queues);
    }
}
