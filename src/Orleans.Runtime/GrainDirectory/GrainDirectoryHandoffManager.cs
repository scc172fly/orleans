using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime.Scheduler;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 此类的大多数方法都进行了同步，因为它们可能同时由
    /// CacheValidator.SchedulingContext 上的 LocalGrainDirectory 和 RemoteGrainDirectory 调用。
    /// </summary>
    internal sealed partial class GrainDirectoryHandoffManager
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
        private readonly LocalGrainDirectory localDirectory;
        private readonly ISiloStatusOracle siloStatusOracle;
        private readonly IClusterMembershipService clusterMembershipService;
        private readonly IInternalGrainFactory grainFactory;
        private readonly ILogger logger;
        private readonly object _lock = new();
        private readonly Queue<(string name, object state, Func<GrainDirectoryHandoffManager, object, Task> action)> pendingOperations = new();
        private readonly AsyncLock executorLock = new AsyncLock();

        internal GrainDirectoryHandoffManager(
            LocalGrainDirectory localDirectory,
            ISiloStatusOracle siloStatusOracle,
            IClusterMembershipService clusterMembershipService,
            IInternalGrainFactory grainFactory,
            ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<GrainDirectoryHandoffManager>();
            this.localDirectory = localDirectory;
            this.siloStatusOracle = siloStatusOracle;
            this.clusterMembershipService = clusterMembershipService;
            this.grainFactory = grainFactory;
        }

        internal void ProcessSiloAddEvent(SiloAddress addedSilo)
        {
            lock (_lock)
            {
                LogDebugProcessingSiloAddEvent(logger, addedSilo);

                // 检查它是否为当前 Silo 的直接后继（即当前 Silo 是否应该持有该 Silo 的副本）。
                // （如果是，则通过在旧后继与新后继之间拆分分区，调整本地和/或复制的目录分区。）
                // 注意：如果新 Silo 是直接后继，则需要将本地目录的一部分迁移到该 Silo。
                var successor = localDirectory.FindSuccessor(localDirectory.MyAddress);
                if (successor is null || !successor.Equals(addedSilo))
                {
                    LogDebugNotImmediateSuccessor(logger, addedSilo);
                    return;
                }

                // 拆分本地目录，并将属于新直接后继的份额发送给它。
                LogDebugSplittingPartition(logger, addedSilo);
                var splitPartListSingle = localDirectory.DirectoryPartition.Split(
                    grain =>
                    {
                        var s = localDirectory.CalculateGrainDirectoryPartition(grain);
                        return s != null && !localDirectory.MyAddress.Equals(s);
                    });

                EnqueueOperation($"{nameof(ProcessSiloAddEvent)}({addedSilo})", addedSilo,
                    (t, state) => t.ProcessAddedSiloAsync((SiloAddress)state, splitPartListSingle));
            }
        }

        private async Task ProcessAddedSiloAsync(SiloAddress addedSilo, List<GrainAddress> splitPartListSingle)
        {
            if (!this.localDirectory.Running) return;
            if (!addedSilo.Equals(localDirectory.FindSuccessor(localDirectory.MyAddress)))
            {
                LogDebugNotImmediateSuccessor(logger, addedSilo);
                return;
            }

            if (this.siloStatusOracle.GetApproximateSiloStatus(addedSilo) == SiloStatus.Active)
            {
                if (splitPartListSingle.Count > 0)
                {
                    LogDebugSendingEntries(logger, splitPartListSingle.Count, addedSilo);
                    LogInformationSendingSplitPartition(logger, splitPartListSingle.Count, addedSilo);
                }

                try
                {
                    await localDirectory.GetDirectoryReference(addedSilo)
                        .AcceptSplitPartition(splitPartListSingle, CancellationToken.None);
                    if (splitPartListSingle.Count > 0)
                    {
                        LogInformationCompletedSplitPartition(logger, splitPartListSingle.Count, addedSilo);
                    }
                }
                catch (Exception exception)
                {
                    LogWarningSplitPartitionFailed(logger, exception, splitPartListSingle.Count, addedSilo);
                    throw;
                }
            }
            else
            {
                LogWarningSiloNotActive(logger, addedSilo);
                return;
            }

            if (splitPartListSingle.Count > 0)
            {
                LogDebugRemovingEntries(logger, splitPartListSingle.Count);

                foreach (var activationAddress in splitPartListSingle)
                {
                    localDirectory.DirectoryPartition.RemoveGrain(activationAddress.GrainId);
                }

                LogInformationRemovedTransferredEntries(logger, splitPartListSingle.Count, addedSilo);
            }
        }

        internal void AcceptExistingRegistrations(List<GrainAddress> singleActivations)
        {
            if (singleActivations.Count == 0) return;
            EnqueueOperation(nameof(AcceptExistingRegistrations), singleActivations,
                (t, state) => t.AcceptExistingRegistrationsAsync((List<GrainAddress>)state));
        }

        private async Task AcceptExistingRegistrationsAsync(List<GrainAddress> singleActivations)
        {
            if (!this.localDirectory.Running) return;

            var snapshot = this.clusterMembershipService.CurrentSnapshot;
            for (var i = singleActivations.Count - 1; i >= 0; i--)
            {
                if (!IsTransferableRegistration(singleActivations[i], snapshot))
                {
                    singleActivations.RemoveAt(i);
                }
            }

            if (singleActivations.Count == 0) return;

            LogDebugAcceptingRegistrations(logger, singleActivations.Count);

            var tasks = singleActivations.Select(addr => this.localDirectory.RegisterAsync(addr, previousAddress: null, 1)).ToArray();
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception exception)
            {
                LogWarningExceptionRegistering(logger, exception);
                throw;
            }
            finally
            {
                Dictionary<SiloAddress, List<GrainAddress>>? duplicates = null;
                for (var i = tasks.Length - 1; i >= 0; i--)
                {
                    // 下次重试失败的任务。
                    if (!tasks[i].IsCompletedSuccessfully) continue;

                    // 记录注册竞争中失败的应用实例（重复激活实例）。
                    var winner = await tasks[i];
                    if (winner.Address is not { } winnerAddress || !winnerAddress.Equals(singleActivations[i]))
                    {
                        var duplicate = singleActivations[i];
                        (CollectionsMarshal.GetValueRefOrAddDefault(duplicates ??= new(), duplicate.SiloAddress!, out _) ??= new()).Add(duplicate);
                    }

                    // 移除已完成的任务。
                    singleActivations.RemoveAt(i);
                }

                // 将重复激活实例的清理排在当前移交操作之后。
                QueueDuplicateActivationDestruction(duplicates);
            }
        }

        private void QueueDuplicateActivationDestruction(Dictionary<SiloAddress, List<GrainAddress>>? duplicates)
        {
            if (duplicates == null || duplicates.Count == 0) return;
            EnqueueOperation(nameof(QueueDuplicateActivationDestruction), duplicates,
                (t, state) => t.DestroyDuplicateActivationsAsync((Dictionary<SiloAddress, List<GrainAddress>>)state));
        }

        private async Task DestroyDuplicateActivationsAsync(Dictionary<SiloAddress, List<GrainAddress>> duplicates)
        {
            while (duplicates.Count > 0)
            {
                var pair = duplicates.FirstOrDefault();
                if (this.siloStatusOracle.GetApproximateSiloStatus(pair.Key) == SiloStatus.Active)
                {
                    LogDebugDestroyingDuplicates(logger, duplicates.Count, pair.Key, new(pair.Value));

                    var remoteCatalog = this.grainFactory.GetSystemTarget<ICatalog>(Constants.CatalogType, pair.Key);
                    await remoteCatalog.DeleteActivations(
                        pair.Value,
                        DeactivationReasonCode.DuplicateActivation,
                        "This grain has been activated elsewhere",
                        CancellationToken.None);
                }

                duplicates.Remove(pair.Key);
            }
        }

        internal static bool IsTransferableRegistration(GrainAddress address, ClusterMembershipSnapshot snapshot)
        {
            if (address.SiloAddress is not { } silo)
            {
                return false;
            }

            return snapshot.GetSiloStatus(silo, address.MembershipVersion) != SiloStatus.Dead;
        }

        private void EnqueueOperation(string name, object state, Func<GrainDirectoryHandoffManager, object, Task> action)
        {
            lock (_lock)
            {
                this.pendingOperations.Enqueue((name, state, action));
                if (this.pendingOperations.Count <= 2)
                {
                    this.localDirectory.RemoteGrainDirectory.WorkItemGroup.QueueTask(ExecutePendingOperations, localDirectory.RemoteGrainDirectory);
                }
            }
        }

        private async Task ExecutePendingOperations()
        {
            using (await executorLock.LockAsync())
            {
                while (true)
                {
                    // 获取下一项操作，如果没有操作则退出。
                    (string Name, object State, Func<GrainDirectoryHandoffManager, object, Task> Action) op;
                    lock (_lock)
                    {
                        if (this.pendingOperations.Count == 0) break;

                        op = this.pendingOperations.Peek();
                    }

                    try
                    {
                        await op.Action(this, op.State);
                        lock (_lock)
                        {
                            this.pendingOperations.Dequeue();
                        }
                    }
                    catch (Exception exception)
                    {
                        if (!this.localDirectory.Running)
                        {
                            return;
                        }

                        LogWarningOperationFailedRetry(logger, exception, op.Name);
                        await Task.Delay(RetryDelay);
                        if (!this.localDirectory.Running)
                        {
                            return;
                        }

                        // 持续重试失败的移交工作，但允许后续排队的操作继续推进。
                        lock (_lock)
                        {
                            this.pendingOperations.Dequeue();
                            this.pendingOperations.Enqueue(op);
                        }
                    }
                }
            }
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Processing silo add event for {AddedSilo}"
        )]
        private static partial void LogDebugProcessingSiloAddEvent(ILogger logger, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "{AddedSilo} is not my immediate successor."
        )]
        private static partial void LogDebugNotImmediateSuccessor(ILogger logger, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Splitting my partition between me and {AddedSilo}"
        )]
        private static partial void LogDebugSplittingPartition(ILogger logger, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Sending {Count} single activation entries to {AddedSilo}"
        )]
        private static partial void LogDebugSendingEntries(ILogger logger, int count, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Rolling upgrade diagnostic: sending {Count} split-partition registrations to {AddedSilo}."
        )]
        private static partial void LogInformationSendingSplitPartition(ILogger logger, int count, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Rolling upgrade diagnostic: completed split-partition transfer of {Count} registrations to {AddedSilo}."
        )]
        private static partial void LogInformationCompletedSplitPartition(ILogger logger, int count, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Rolling upgrade diagnostic: split-partition transfer of {Count} registrations to {AddedSilo} failed."
        )]
        private static partial void LogWarningSplitPartitionFailed(ILogger logger, Exception exception, int count, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Silo {AddedSilo} is no longer active and therefore cannot receive this partition split"
        )]
        private static partial void LogWarningSiloNotActive(ILogger logger, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Removing {Count} single activation after partition split"
        )]
        private static partial void LogDebugRemovingEntries(ILogger logger, int count);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Rolling upgrade diagnostic: removed {Count} transferred registrations after handoff to {AddedSilo}."
        )]
        private static partial void LogInformationRemovedTransferredEntries(ILogger logger, int count, SiloAddress addedSilo);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(AcceptExistingRegistrations)}: accepting {{Count}} single-activation registrations"
        )]
        private static partial void LogDebugAcceptingRegistrations(ILogger logger, int count);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = $"Exception registering activations in {nameof(AcceptExistingRegistrations)}"
        )]
        private static partial void LogWarningExceptionRegistering(ILogger logger, Exception exception);

        private readonly struct GrainAddressesLogValue(List<GrainAddress> grainAddresses)
        {
            public override string ToString() => string.Join("\n * ", grainAddresses.Select(_ => _));
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = $"{nameof(QueueDuplicateActivationDestruction)} will destroy {{Count}} duplicate activations on silo {{SiloAddress}}: {{Duplicates}}"
        )]
        private static partial void LogDebugDestroyingDuplicates(ILogger logger, int count, SiloAddress siloAddress, GrainAddressesLogValue duplicates);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "{Operation} failed, will be retried"
        )]
        private static partial void LogWarningOperationFailedRetry(ILogger logger, Exception exception, string operation);

    }
}
