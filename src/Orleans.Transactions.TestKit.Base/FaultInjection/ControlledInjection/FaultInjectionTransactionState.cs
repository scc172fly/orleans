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
    /// 配置下一个匹配交易阶段的输入错误 。
    /// </summary>
    [GenerateSerializer]
    public class FaultInjectionControl
    {
        /// <summary>
        /// 获取或设置输入过错的交易阶段。
        /// </summary>
        [Id(0)]
        public TransactionFaultInjectPhase FaultInjectionPhase = TransactionFaultInjectPhase.None;

        /// <summary>
        /// 获取或设置注射的错误类型。
        /// </summary>
        [Id(1)]
        public FaultInjectionType FaultInjectionType = FaultInjectionType.None;

        /// <summary>
        /// 清除配置的过失注射。
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
    /// 指明了可以注入过失的交易阶段。
    /// </summary>
    [GenerateSerializer]
    public enum TransactionFaultInjectPhase
    {
        /// <summary>
        /// 未选择交易阶段 。
        /// </summary>
        None,

        /// <summary>
        /// 实施只读交易后,会发生过失。
        /// </summary>
        AfterCommitReadOnly,

        /// <summary>
        /// 准备交易后发生过失。
        /// </summary>
        AfterPrepare,

        /// <summary>
        /// 准备和进行交易后,发生过失。
        /// </summary>
        AfterPrepareAndCommit,

        /// <summary>
        /// 中止交易后发生错误。
        /// </summary>
        AfterAbort,

        /// <summary>
        /// 在收到预先通知后,即发生过失。
        /// </summary>
        AfterPrepared,

        /// <summary>
        /// 注销交易后发生错误。
        /// </summary>
        AfterCancel,

        /// <summary>
        /// 证实交易后,即产生过失。
        /// </summary>
        AfterConfirm,

        /// <summary>
        /// 处理交易按键后,发生故障。
        /// </summary>
        AfterPing,

        /// <summary>
        /// 在确认交易前先输入错误。
        /// </summary>
        BeforeConfirm,

        /// <summary>
        /// 准备交易前先输入错误。
        /// </summary>
        BeforePrepare,

        /// <summary>
        /// 准备和进行交易前先输入过失。
        /// </summary>
        BeforePrepareAndCommit
    }

    /// <summary>
    /// 指明了将错误注入交易。
    /// </summary>
    public enum FaultInjectionType
    {
        /// <summary>
        /// 没有错注射。
        /// </summary>
        None,

        /// <summary>
        /// 在选定的交易阶段之后,关闭 grain 。
        /// </summary>
        Deactivation,

        /// <summary>
        /// 在存储交易状态之前, 丢弃了存储例外 。
        /// </summary>
        ExceptionBeforeStore,

        /// <summary>
        /// 在存储交易状态后, 将存储例外丢弃 。
        /// </summary>
        ExceptionAfterStore,

        /// <summary>
        /// 在存储交易状态后, 抛出一个通用例外。
        /// </summary>
        GenericExceptionAfterStore
    }

    /// <summary>
    /// 提供交易状态,其操作可注入受控过失。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    public interface IFaultInjectionTransactionalState<TState> : ITransactionalState<TState> where TState : class, new()
    {
        /// <summary>
        /// 设置或设置错误注射配置。
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
            // 将资源工厂添加到 grain 上下文
            context.RegisterResourceFactory<ITransactionalResource>(stateName, () => new FaultInjectionTransactionalResource<TState>(this.faultInjector, FaultInjectionControl, new TransactionalResource<TState>(queue), context, logger, grainRuntime));

            // grain 上下文添加 tm 工厂
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
