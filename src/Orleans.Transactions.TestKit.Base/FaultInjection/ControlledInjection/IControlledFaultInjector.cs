using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 将过失注入下一个交易性国家储存操作的控制。
    /// </summary>
    public interface IControlledTransactionFaultInjector : ITransactionFaultInjector
    {
        /// <summary>
        /// 获取或设定一个数值,表明是否在存储交易状态之前放弃存储例外。
        /// </summary>
        bool InjectBeforeStore { get; set; }

        /// <summary>
        /// 获取或设定一个数值,表明是否在存储交易状态后放弃存储例外。
        /// </summary>
        bool InjectAfterStore { get; set; }

        /// <summary>
        /// 获取或设定一个数值,表明是否在存储交易状态后放弃通用例外。
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
