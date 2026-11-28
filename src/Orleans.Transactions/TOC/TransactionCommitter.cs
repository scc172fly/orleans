using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Timers.Internal;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.Diagnostics;
using Orleans.Transactions.State;
using Orleans.Transactions.TOC;

namespace Orleans.Transactions
{
    /// <summary>
    /// 将服务操作登记为存储操作的前置条件；这些操作在提交期间、作出持久化提交决策之前执行。
    /// </summary>
    /// <typeparam name="TService">接收已提交操作的服务类型。</typeparam>
    public partial class TransactionCommitter<TService> : ITransactionCommitter<TService>, ILifecycleParticipant<IGrainLifecycle>
        where TService : class
    {
        private readonly ITransactionCommitterConfiguration config;
        private readonly IGrainContext context;
        private readonly ITransactionDataCopier<OperationState> copier;
        private readonly IGrainRuntime grainRuntime;
        private readonly ILogger logger;
        private ParticipantId participantId;
        private TransactionQueue<OperationState> queue = null!;

        private bool detectReentrancy;

        /// <summary>
        /// 初始化 <see cref="TransactionCommitter{TService}"/> 类的新实例。
        /// </summary>
        /// <param name="config">提交器配置。</param>
        /// <param name="contextAccessor">当前 Grain 激活实例上下文的访问器。</param>
        /// <param name="copier">用于隔离待提交操作的复制器。</param>
        /// <param name="grainRuntime">Grain 运行时。</param>
        /// <param name="logger">日志记录器。</param>
        public TransactionCommitter(
            ITransactionCommitterConfiguration config,
            IGrainContextAccessor contextAccessor,
            ITransactionDataCopier<OperationState> copier,
            IGrainRuntime grainRuntime,
            ILogger<TransactionCommitter<TService>> logger)
        {
            this.config = config;
            this.context = contextAccessor.GrainContext;
            this.copier = copier;
            this.grainRuntime = grainRuntime;
            this.logger = logger;
        }

        /// <inheritdoc/>
        public Task OnCommit(ITransactionCommitOperation<TService> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (detectReentrancy)
            {
                throw new LockRecursionException("cannot perform an update operation from within another operation");
            }

            var info = TransactionContext.GetRequiredTransactionInfo();

            LogTraceStartWrite(info);
            if (info.IsReadOnly)
            {
                throw new OrleansReadOnlyViolatedException(info.Id);
            }

            info.Participants.TryGetValue(this.participantId, out var recordedaccesses);

            return this.queue.RWLock.EnterLock<bool>(info.TransactionId, info.Priority, info.Timeout, recordedaccesses, false, info.UseExclusiveLock,
                () =>
                {
                    // 检查事务是否在等待期间过期。
                    if (!this.queue.RWLock.TryGetRecord(info.TransactionId, out TransactionRecord<OperationState>? record))
                    {
                        throw new OrleansCascadingAbortException(info.TransactionId.ToString());
                    }

                    // 将当前时钟合并到事务时间戳中。
                    record.Timestamp = this.queue.Clock.MergeUtcNow(info.TimeStamp);

                    // 链接到最新状态。
                    if (record.State == null)
                    {
                        this.queue.GetMostRecentState(out record.State, out record.SequenceNumber);
                    }

                    // 如果这是第一次写入，则创建状态的深拷贝。
                    if (!record.HasCopiedState)
                    {
                        record.State = this.copier.DeepCopy(record.State);
                        record.SequenceNumber++;
                        record.HasCopiedState = true;
                    }

                    LogDebugUpdateLockWrite(record.SequenceNumber, record.TransactionId, new(record.Timestamp));

                    // 在事务信息数据结构中记录此次写入。
                    info.RecordWrite(this.participantId, record.Timestamp);

                    // 执行写入。
                    try
                    {
                        detectReentrancy = true;

                        record.State.Operation = operation;
                        return true;
                    }
                    finally
                    {
                        LogTraceEndWrite(info, record.TransactionId, new(record.Timestamp));
                        detectReentrancy = false;
                    }
                }
            );
        }

        /// <inheritdoc/>
        public void Participate(IGrainLifecycle lifecycle)
        {
            lifecycle.Subscribe<TransactionalState<OperationState>>(GrainLifecycleStage.SetupState, OnSetupState, OnStop);
            lifecycle.Subscribe<TransactionalState<OperationState>>(GrainLifecycleStage.Last, static _ => Task.CompletedTask, OnStop);
        }

        // 初始化可能在创建队列之前失败。
        private Task OnStop(CancellationToken ct) => this.queue?.StopAsync(ct) ?? Task.CompletedTask;

        private async Task OnSetupState(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            this.participantId = new ParticipantId(this.config.ServiceName, this.context.GrainReference, ParticipantId.Role.Resource | ParticipantId.Role.PriorityManager);

            var storageFactory = this.context.ActivationServices.GetRequiredService<INamedTransactionalStateStorageFactory>();
            ITransactionalStateStorage<OperationState> storage = storageFactory.Create<OperationState>(this.config.StorageName, this.config.ServiceName);

            // 初始化事务处理管道。
            void deactivate() => grainRuntime.DeactivateOnIdle(context);
            var options = this.context.ActivationServices.GetRequiredService<IOptions<TransactionalStateOptions>>();
            var clock = this.context.ActivationServices.GetRequiredService<IClock>();
            TService service = this.context.ActivationServices.GetRequiredKeyedService<TService>(this.config.ServiceName);
            var timerManager = this.context.ActivationServices.GetRequiredService<ITimerManager>();
            var diagnosticIdentity = new TransactionDiagnosticEvents.TransactionDiagnosticIdentity(
                this.context.Address.SiloAddress,
                this.context.ActivationId);
            this.queue = new TocTransactionQueue<TService>(
                service,
                options,
                this.participantId,
                deactivate,
                storage,
                clock,
                logger,
                timerManager,
                diagnosticIdentity);

            // 将事务管理器工厂添加到 Grain 上下文中。
            this.context.RegisterResourceFactory<ITransactionManager>(this.config.ServiceName, () => new TransactionManager<OperationState>(this.queue));

            // 恢复状态。
            await this.queue.NotifyOfRestore();
        }

        /// <summary>
        /// 存储与事务记录关联的提交操作。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class OperationState
        {
            /// <summary>
            /// 获取或设置事务提交时要执行的操作。
            /// </summary>
            [Id(0)]
            public ITransactionCommitOperation<TService> Operation { get; set; } = null!;
        }

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "StartWrite {Info}"
        )]
        private partial void LogTraceStartWrite(TransactionInfo info);

        private readonly struct DateTimeLogRecord(DateTime ts)
        {
            public override string ToString() => ts.ToString("o");
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Update-lock write v{SequenceNumber} {TransactionId} {Timestamp}"
        )]
        private partial void LogDebugUpdateLockWrite(long sequenceNumber, Guid transactionId, DateTimeLogRecord timestamp);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "EndWrite {Info} {TransactionId} {Timestamp}"
        )]
        private partial void LogTraceEndWrite(TransactionInfo info, Guid transactionId, DateTimeLogRecord timestamp);
    }
}
