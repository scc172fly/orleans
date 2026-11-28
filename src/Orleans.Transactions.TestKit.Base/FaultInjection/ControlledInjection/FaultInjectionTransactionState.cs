using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.State;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 配置在下一次匹配的事务阶段中注入的故障。
    /// </summary>
    [GenerateSerializer]
    public class FaultInjectionControl
    {
        /// <summary>
        /// 获取或设置注入故障的事务阶段。
        /// </summary>
        [Id(0)]
        public TransactionFaultInjectPhase FaultInjectionPhase = TransactionFaultInjectPhase.None;

        /// <summary>
        /// 获取或设置要注入的故障类型。
        /// </summary>
        [Id(1)]
        public FaultInjectionType FaultInjectionType = FaultInjectionType.None;

        /// <summary>
        /// 清除已配置的故障注入。
        /// </summary>
        public void Reset()
        {
            this.FaultInjectionType = FaultInjectionType.None;
            this.FaultInjectionPhase = TransactionFaultInjectPhase.None;
        }

        internal bool TryConsume(TransactionFaultInjectPhase phase, out FaultInjectionType injectionType)
        {
            if (this.FaultInjectionPhase != phase)
            {
                injectionType = FaultInjectionType.None;
                return false;
            }

            injectionType = this.FaultInjectionType;
            this.Reset();
            return true;
        }
    }

    internal readonly record struct FaultInjectionEvent(
        GrainId GrainId,
        Guid TransactionId,
        TransactionFaultInjectPhase Phase,
        FaultInjectionType Type);

    internal static class FaultInjectionDiagnosticEvents
    {
        private static readonly object LockObj = new();
        private static ImmutableArray<Action<FaultInjectionEvent>> observers = [];

        public static IDisposable Subscribe(Action<FaultInjectionEvent> observer)
        {
            lock (LockObj)
            {
                observers = observers.Add(observer);
            }

            return new Subscription(observer);
        }

        public static void Emit(FaultInjectionEvent evt)
        {
            ImmutableArray<Action<FaultInjectionEvent>> snapshot;
            lock (LockObj)
            {
                snapshot = observers;
            }

            foreach (var observer in snapshot)
            {
                observer(evt);
            }
        }

        private sealed class Subscription(Action<FaultInjectionEvent> observer) : IDisposable
        {
            private Action<FaultInjectionEvent>? observer = observer;

            public void Dispose()
            {
                var value = Interlocked.Exchange(ref this.observer, null);
                if (value is null)
                {
                    return;
                }

                lock (LockObj)
                {
                    observers = observers.Remove(value);
                }
            }
        }
    }

    /// <summary>
    /// 标识可以注入故障的事务阶段。
    /// </summary>
    [GenerateSerializer]
    public enum TransactionFaultInjectPhase
    {
        /// <summary>
        /// 未选择事务阶段。
        /// </summary>
        None,

        /// <summary>
        /// 提交只读事务后注入故障。
        /// </summary>
        AfterCommitReadOnly,

        /// <summary>
        /// 准备事务后注入故障。
        /// </summary>
        AfterPrepare,

        /// <summary>
        /// 准备并提交事务后注入故障。
        /// </summary>
        AfterPrepareAndCommit,

        /// <summary>
        /// 中止事务后注入故障。
        /// </summary>
        AfterAbort,

        /// <summary>
        /// 收到准备完成通知后注入故障。
        /// </summary>
        AfterPrepared,

        /// <summary>
        /// 取消事务后注入故障。
        /// </summary>
        AfterCancel,

        /// <summary>
        /// 确认事务后注入故障。
        /// </summary>
        AfterConfirm,

        /// <summary>
        /// 处理事务探测请求后注入故障。
        /// </summary>
        AfterPing,

        /// <summary>
        /// 确认事务前注入故障。
        /// </summary>
        BeforeConfirm,

        /// <summary>
        /// 准备事务前注入故障。
        /// </summary>
        BeforePrepare,

        /// <summary>
        /// 准备并提交事务前注入故障。
        /// </summary>
        BeforePrepareAndCommit
    }

    /// <summary>
    /// 标识要注入事务的故障。
    /// </summary>
    public enum FaultInjectionType
    {
        /// <summary>
        /// 不注入故障。
        /// </summary>
        None,

        /// <summary>
        /// 在选定事务阶段之后停用 Grain。
        /// </summary>
        Deactivation,

        /// <summary>
        /// 在存储事务状态之前引发存储异常。
        /// </summary>
        ExceptionBeforeStore,

        /// <summary>
        /// 在存储事务状态之后引发存储异常。
        /// </summary>
        ExceptionAfterStore,

        /// <summary>
        /// 在存储事务状态之后引发一般异常。
        /// </summary>
        GenericExceptionAfterStore
    }

    /// <summary>
    /// 提供操作时能够注入受控故障的事务状态。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    public interface IFaultInjectionTransactionalState<TState> : ITransactionalState<TState> where TState : class, new()
    {
        /// <summary>
        /// 获取或设置故障注入配置。
        /// </summary>
        FaultInjectionControl FaultInjectionControl { get; set; }
    }

    internal class FaultInjectionTransactionalState<TState> : IFaultInjectionTransactionalState<TState>, ILifecycleParticipant<IGrainLifecycle>
        where TState : class, new()
    {
        private readonly IGrainRuntime grainRuntime;
        private readonly TransactionalState<TState> txState;
        private readonly ILogger logger;
        public FaultInjectionControl FaultInjectionControl { get; set; }
        private readonly IControlledTransactionFaultInjector faultInjector;
        public string CurrentTransactionId => this.txState.CurrentTransactionId;
        public FaultInjectionTransactionalState(TransactionalState<TState> txState, IControlledTransactionFaultInjector faultInjector, IGrainRuntime grainRuntime, ILogger<FaultInjectionTransactionalState<TState>> logger)
        {
            this.grainRuntime = grainRuntime;
            this.txState = txState;
            this.logger = logger;
            this.FaultInjectionControl = new FaultInjectionControl();
            this.faultInjector = faultInjector;
        }

        public void Participate(IGrainLifecycle lifecycle)
        {
            this.txState.Participate(lifecycle, this.SetupResourceFactory);
        }

        internal void SetupResourceFactory(IGrainContext context, string stateName, TransactionQueue<TState> queue)
        {
            // 将资源工厂添加到 Grain 上下文
            context.RegisterResourceFactory<ITransactionalResource>(stateName, () => new FaultInjectionTransactionalResource<TState>(this.faultInjector, FaultInjectionControl, new TransactionalResource<TState>(queue), context, logger, grainRuntime));

            // 将事务管理器工厂添加到 Grain 上下文
            context.RegisterResourceFactory<ITransactionManager>(stateName, () => new FaultInjectionTransactionManager<TState>(this.faultInjector, FaultInjectionControl, new TransactionManager<TState>(queue), context, logger, grainRuntime));
        }

        public Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction)
        {
            return this.txState.PerformRead(readFunction);
        }

        public Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction, ReadIsolation isolation)
        {
            return this.txState.PerformRead(readFunction, isolation);
        }

        public Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction)
        {
            return this.txState.PerformUpdate(updateFunction);
        }
    }
}
