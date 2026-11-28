using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 控制下一次事务状态存储操作中注入的故障。
    /// </summary>
    public interface IControlledTransactionFaultInjector : ITransactionFaultInjector
    {
        /// <summary>
        /// 获取或设置一个值，指示是否在存储事务状态之前引发存储异常。
        /// </summary>
        bool InjectBeforeStore { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示是否在存储事务状态之后引发存储异常。
        /// </summary>
        bool InjectAfterStore { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示是否在存储事务状态之后引发一般异常。
        /// </summary>
        bool InjectGenericAfterStore { get; set; }
    }

    internal interface ITransactionScopedFaultInjector
    {
        void Arm(Guid transactionId, FaultInjectionType injectionType, bool requireTransactionMatch);
        void BeforeStore(ImmutableArray<Guid> transactionIds);
        void AfterStore(ImmutableArray<Guid> transactionIds);
    }

    internal static class ControlledTransactionFaultInjectorExtensions
    {
        public static void Arm(
            this IControlledTransactionFaultInjector faultInjector,
            Guid transactionId,
            FaultInjectionType injectionType,
            bool requireTransactionMatch = false)
        {
            if (faultInjector is ITransactionScopedFaultInjector scopedFaultInjector)
            {
                scopedFaultInjector.Arm(transactionId, injectionType, requireTransactionMatch);
                return;
            }

            faultInjector.InjectBeforeStore = injectionType == FaultInjectionType.ExceptionBeforeStore;
            faultInjector.InjectAfterStore = injectionType == FaultInjectionType.ExceptionAfterStore;
            faultInjector.InjectGenericAfterStore = injectionType == FaultInjectionType.GenericExceptionAfterStore;
        }

        public static ImmutableArray<Guid> GetTransactionIds<TState>(
            TransactionalStateMetaData metadata,
            List<PendingTransactionState<TState>>? statesToPrepare)
            where TState : class, new()
        {
            var result = ImmutableArray.CreateBuilder<Guid>(
                metadata.CommitRecords.Count + (statesToPrepare?.Count ?? 0));
            result.AddRange(metadata.CommitRecords.Keys);
            if (statesToPrepare is not null)
            {
                foreach (var state in statesToPrepare)
                {
                    if (Guid.TryParse(state.TransactionId, out var transactionId))
                    {
                        result.Add(transactionId);
                    }
                }
            }

            return result.MoveToImmutable();
        }

        public static void BeforeStore(
            this ITransactionFaultInjector faultInjector,
            ImmutableArray<Guid> transactionIds)
        {
            if (faultInjector is ITransactionScopedFaultInjector scopedFaultInjector)
            {
                scopedFaultInjector.BeforeStore(transactionIds);
            }
            else
            {
                faultInjector.BeforeStore();
            }
        }

        public static void AfterStore(
            this ITransactionFaultInjector faultInjector,
            ImmutableArray<Guid> transactionIds)
        {
            if (faultInjector is ITransactionScopedFaultInjector scopedFaultInjector)
            {
                scopedFaultInjector.AfterStore(transactionIds);
            }
            else
            {
                faultInjector.AfterStore();
            }
        }
    }
}
