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
    /// 将服务业务列为储存的先决条件,在作出持久承诺决定之前实施。
    /// </summary>
    /// <typeparam name="TService">接受已承诺作业的服务类型。</typeparam>
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
        /// <param name="config">承诺方配置。</param>
        /// <param name="contextAccessor">当前 grain 激活环境的存取器。</param>
        /// <param name="copier">复印机用来分离待决承付业务。</param>
        /// <param name="grainRuntime">运行时间 grain 。</param>
        /// <param name="logger">日志器。</param>
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
                    // 检查等待时是否过期
                    if (!this.queue.RWLock.TryGetRecord(info.TransactionId, out TransactionRecord<OperationState>? record))
                    {
                        throw new OrleansCascadingAbortException(info.TransactionId.ToString());
                    }

                    // 将当前时钟合并到交易时间戳
                    record.Timestamp = this.queue.Clock.MergeUtcNow(info.TimeStamp);

                    // 与最新状态的链接
                    if (record.State == null)
                    {
                        this.queue.GetMostRecentState(out record.State, out record.SequenceNumber);
                    }

                    // 如果这是首篇写作, 请填写一份深度的状态副本
                    if (!record.HasCopiedState)
                    {
                        record.State = this.copier.DeepCopy(record.State);
                        record.SequenceNumber++;
                        record.HasCopiedState = true;
                    }

                    LogDebugUpdateLockWrite(record.SequenceNumber, record.TransactionId, new(record.Timestamp));

                    // 记录此交易信息数据结构中的写入
                    info.RecordWrite(this.participantId, record.Timestamp);

                    // 执行写作
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

        // 创建队列前设置可能失败 。
        private Task OnStop(CancellationToken ct) => this.queue?.StopAsync(ct) ?? Task.CompletedTask;

        private async Task OnSetupState(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            this.participantId = new ParticipantId(this.config.ServiceName, this.context.GrainReference, ParticipantId.Role.Resource | ParticipantId.Role.PriorityManager);

            var storageFactory = this.context.ActivationServices.GetRequiredService<INamedTransactionalStateStorageFactory>();
            ITransactionalStateStorage<OperationState> storage = storageFactory.Create<OperationState>(this.config.StorageName, this.config.ServiceName);

            // 设置交易处理管道
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

            // 将交易管理器工厂添加到 grain 上下文
            this.context.RegisterResourceFactory<ITransactionManager>(this.config.ServiceName, () => new TransactionManager<OperationState>(this.queue));

            // 恢复状态
            await this.queue.NotifyOfRestore();
        }

        /// <summary>
        /// 储存与交易记录有关的承付业务。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class OperationState
        {
            /// <summary>
            /// 获取或设置交易发生时要应用的操作。
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
