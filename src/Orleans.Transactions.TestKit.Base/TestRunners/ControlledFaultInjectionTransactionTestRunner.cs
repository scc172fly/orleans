using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Orleans.Configuration;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行在受控协议阶段注入故障并验证恢复行为的事务测试。
    /// </summary>
    public class ControlledFaultInjectionTransactionTestRunner : TransactionTestRunnerBase
    {
        private static readonly TimeSpan RecoveryWatchdog =
            new ClientMessagingOptions().ResponseTimeout
            + TransactionalStateOptions.DefaultPrepareTimeout
            + TransactionalStateOptions.DefaultRemoteTransactionPingFrequency
            + TransactionalStateOptions.DefaultLockTimeout;

        /// <summary>
        /// 初始化 <see cref="ControlledFaultInjectionTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        public ControlledFaultInjectionTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
         : base(grainFactory, output)
        { }

        /// <summary>
        /// 验证 Grain 停用并重新激活后，已提交的值仍可读取。
        /// </summary>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task SingleGrainReadTransaction()
        {
            const int expected = 5;

            IFaultInjectionTransactionTestGrain grain = grainFactory.GetGrain<IFaultInjectionTransactionTestGrain>(Guid.NewGuid());
            await grain.Set(expected);
            int actual = await grain.Get();
            actual.Should().Be(expected);
            await grain.Deactivate();
            actual = await grain.Get();
            actual.Should().Be(expected);
        }

        /// <summary>
        /// 验证 Grain 停用并重新激活后，事务写入仍处于已提交状态。
        /// </summary>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task SingleGrainWriteTransaction()
        {
            const int delta = 5;
            IFaultInjectionTransactionTestGrain grain = this.grainFactory.GetGrain<IFaultInjectionTransactionTestGrain>(Guid.NewGuid());
            int original = await grain.Get();
            await grain.Add(delta);
            await grain.Deactivate();
            int expected = original + delta;
            int actual = await grain.Get();
            actual.Should().Be(expected);
        }

        /// <summary>
        /// 在选定协议阶段向多 Grain 写入注入故障，并验证恢复后的状态。
        /// </summary>
        /// <param name="injectionPhase">要注入故障的事务协议阶段。</param>
        /// <param name="injectionType">要注入的故障类型。</param>
        /// <returns>表示故障注入与恢复测试的任务。</returns>
        public virtual async Task MultiGrainWriteTransaction_FaultInjection(TransactionFaultInjectPhase injectionPhase, FaultInjectionType injectionType)
        {
            const int setval = 5;
            const int addval = 7;
            int? expected = setval + addval;
            const int grainCount = TransactionTestConstants.MaxCoordinatedTransactions;
            var faultInjectionControl = new FaultInjectionControl() { FaultInjectionPhase = injectionPhase, FaultInjectionType = injectionType };
            List<IFaultInjectionTransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => this.grainFactory.GetGrain<IFaultInjectionTransactionTestGrain>(Guid.NewGuid()))
                    .ToList();

            IFaultInjectionTransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<IFaultInjectionTransactionCoordinatorGrain>(Guid.NewGuid());

            var grainIds = grains.Select(grain => grain.GetGrainId()).ToHashSet();
            using var recoveryEvents = new TransactionRecoveryEventObserver(grainIds);
            var faultObserved = new TaskCompletionSource<FaultInjectionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var faultSubscription = FaultInjectionDiagnosticEvents.Subscribe(evt =>
            {
                if (grainIds.Contains(evt.GrainId)
                    && evt.Phase == injectionPhase
                    && evt.Type == injectionType)
                {
                    faultObserved.TrySetResult(evt);
                }
            });
            await this.ExecuteAndWaitForCommit(
                () => coordinator.MultiGrainSet(grains, setval),
                grains.Count,
                recoveryEvents,
                GetDeadline());
            var faultAttemptSequence = recoveryEvents.LatestRelevantSequence;
            var hasStorageFault = injectionType is FaultInjectionType.ExceptionBeforeStore
                or FaultInjectionType.ExceptionAfterStore
                or FaultInjectionType.GenericExceptionAfterStore;
            var waitForRecovery = false;
            try
            {
                await this.ExecuteAndWaitForCommit(
                    () => coordinator.MultiGrainAddAndFaultInjection(grains, addval, faultInjectionControl),
                    grains.Count,
                    recoveryEvents,
                    GetDeadline());
            }
            catch (OrleansTransactionAbortedException exception)
            {
                this.testOutput($"Fault-injected transaction aborted: {exception}");
                expected = setval;
                waitForRecovery = hasStorageFault;
            }
            catch (OrleansTransactionException exception)
            {
                this.testOutput($"Fault-injected transaction failed with an ambiguous outcome: {exception}");
                expected = null;
                waitForRecovery = hasStorageFault;
            }

            var fault = await this.ObserveFaultInjection(
                faultObserved.Task,
                injectionPhase,
                injectionType,
                recoveryEvents,
                GetDeadline());
            if (waitForRecovery)
            {
                var recovery = await recoveryEvents.WaitForRecoveryCompletionAsync(
                    fault.TransactionId,
                    fault.GrainId,
                    faultAttemptSequence,
                    GetDeadline());
                this.testOutput(
                    $"Fault-injected transaction recovery completed. "
                    + TransactionRecoveryEventObserver.FormatTransition(recovery).Trim());
            }

            var actualValues = await this.ReadAfterRecovery(grains, recoveryEvents, GetDeadline());
            actualValues.Should().OnlyContain(value => value == actualValues[0]);
            if (expected is { } expectedValue)
            {
                actualValues.Should().OnlyContain(value => value == expectedValue);
            }
            else
            {
                actualValues.Should().OnlyContain(value => value == setval || value == setval + addval);
            }
        }

        private async Task ExecuteAndWaitForCommit(
            Func<Task> transaction,
            int participantCount,
            TransactionRecoveryEventObserver recoveryEvents,
            long deadline)
        {
            var sequence = recoveryEvents.LatestRelevantSequence;
            await transaction();
            var commit = await recoveryEvents.WaitForCommitConfirmationAsync(sequence, participantCount, deadline);
            this.testOutput(
                $"Transaction commit and participant confirmations completed. "
                + TransactionRecoveryEventObserver.FormatTransition(commit).Trim());
        }

        private async Task<int[]> ReadAfterRecovery(
            List<IFaultInjectionTransactionTestGrain> grains,
            TransactionRecoveryEventObserver recoveryEvents,
            long deadline)
        {
            var attempt = 0;
            while (Stopwatch.GetTimestamp() < deadline)
            {
                attempt++;
                var sequence = recoveryEvents.LatestRelevantSequence;
                try
                {
                    return await Task.WhenAll(grains.Select(grain => grain.Get()));
                }
                catch (Exception exception) when (exception is OrleansTransactionTransientFailureException
                    or OrleansTransactionInDoubtException
                    or TimeoutException)
                {
                    this.testOutput(
                        $"Recovery read {attempt} failed with {exception.GetType().Name}; "
                        + "waiting for transaction recovery progress.");
                    var transition = await recoveryEvents.WaitForNextTransitionAsync(sequence, deadline);
                    this.testOutput(
                        $"Recovery read {attempt} observed progress. "
                        + TransactionRecoveryEventObserver.FormatTransition(transition).Trim());
                }
            }

            throw new TimeoutException(
                $"The fault-injected transaction did not become readable within the protocol-derived "
                + $"{RecoveryWatchdog} watchdog."
                + Environment.NewLine
                + recoveryEvents.FormatTimeline());
        }

        private async Task<FaultInjectionEvent> ObserveFaultInjection(
            Task<FaultInjectionEvent> observation,
            TransactionFaultInjectPhase phase,
            FaultInjectionType type,
            TransactionRecoveryEventObserver recoveryEvents,
            long deadline)
        {
            try
            {
                if (!observation.IsCompleted)
                {
                    var now = Stopwatch.GetTimestamp();
                    if (now >= deadline)
                    {
                        throw new TimeoutException();
                    }

                    return await observation.WaitAsync(Stopwatch.GetElapsedTime(now, deadline));
                }

                return await observation;
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"The configured {type} fault at {phase} was not observed before the "
                    + $"{RecoveryWatchdog} watchdog expired."
                    + Environment.NewLine
                    + recoveryEvents.FormatTimeline());
            }
        }

        private static long GetDeadline()
            => Stopwatch.GetTimestamp() + (long)(RecoveryWatchdog.TotalSeconds * Stopwatch.Frequency);
    }
}
