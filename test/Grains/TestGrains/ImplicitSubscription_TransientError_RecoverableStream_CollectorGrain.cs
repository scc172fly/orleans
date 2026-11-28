using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orleans.Placement;
using Orleans.Providers.Streams.Generator;
using Orleans.Runtime;
using Orleans.Streams;
using TestGrainInterfaces;
using UnitTests.Grains;

namespace TestGrains
{
    [ImplicitStreamSubscription(StreamNamespace)]
    [PreferLocalPlacement]
    public class ImplicitSubscription_TransientError_RecoverableStream_CollectorGrain : Grain<StreamCheckpoint<int>>, IGeneratedEventCollectorGrain
    {
        public const string StreamNamespace = "TransientError_RecoverableStream";

        // 故障注入
        // 模拟简单的暂时性故障。
        // 每种故障仅发生一次。
        // 在关键位置注入故障，以测试可恢复性。
        // - 首次激活 Grain 时。
        // - 激活后、存储起始令牌之前。
        // - 存储起始令牌之后、处理其对应消息之前。
        // - 流处理过程中（本测试为第 33 条消息，实际位置取决于流）。
        // - 流中的最后一条消息。
        private class FireOnNthTry
        {
            private readonly int attemptToFireOn;
            private int tries;

            public FireOnNthTry(int attemptToFireOn)
            {
                this.attemptToFireOn = attemptToFireOn;
            }

            public bool TryFire(Action fireAction)
            {
                tries++;
                if (tries != attemptToFireOn) return false;
                fireAction();
                return true;
            }
        }

        private class FaultsState
        {
            public readonly FireOnNthTry onActivateFault;
            public readonly FireOnNthTry onFirstMessageFault;
            public readonly FireOnNthTry onFirstMessageProcessedFault;
            public readonly FireOnNthTry on33rdMessageFault;
            public readonly FireOnNthTry onLastMessageFault;

            public FaultsState()
            {
                onActivateFault = new FireOnNthTry(1);
                onFirstMessageFault = new FireOnNthTry(1);
                onFirstMessageProcessedFault = new FireOnNthTry(1);
                on33rdMessageFault = new FireOnNthTry(33);
                onLastMessageFault = new FireOnNthTry(1);
            }
        }

        private static readonly ConcurrentDictionary<Guid, FaultsState> FaultInjectionTracker = new ConcurrentDictionary<Guid, FaultsState>();

        private FaultsState? myFaults;
        private FaultsState Faults { get { return myFaults ?? (myFaults = FaultInjectionTracker.GetOrAdd(this.GetPrimaryKey(), key => new FaultsState())); } }

        // Grain 实例状态
        private readonly ILogger logger;
        private IAsyncStream<GeneratedEvent> stream = null!;

        public ImplicitSubscription_TransientError_RecoverableStream_CollectorGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");

            Faults.onActivateFault.TryFire(() => InjectFault(activating: true));
            await ReadStateAsync();

            Guid streamGuid = this.GetPrimaryKey();
            if (State.StreamGuid != streamGuid)
            {
                State.StreamGuid = streamGuid;
                State.StreamNamespace = StreamNamespace;
                await WriteStateAsync();
            }

            var streamProvider = this.GetStreamProvider(GeneratedStreamTestConstants.StreamProviderName);
            stream = streamProvider.GetStream<GeneratedEvent>(State.StreamNamespace!, State.StreamGuid); // 在获取流之前，已在上方初始化命名空间。
            foreach (StreamSubscriptionHandle<GeneratedEvent> handle in await stream.GetAllSubscriptionHandles())
            {
                await handle.ResumeAsync(OnNextAsync, OnErrorAsync, State.RecoveryToken);
            }
        }

        private async Task OnNextAsync(GeneratedEvent evt, StreamSequenceToken? sequenceToken)
        {

            // 忽略重复事件
            if (State.IsDuplicate(sequenceToken!)) // 生成的流始终提供序列令牌。
            {
                logger.LogInformation("Received duplicate event.  StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);
                return;
            }

            logger.LogInformation("Received event.  StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);

            // 尝试注入故障前先递增累加器
            State.Accumulator++;

            // 仅当这是处理的第一个事件时，才更新起始令牌
            // 在这种情况下，需要保存起始令牌，以便发生故障时恢复。
            if (State.TryUpdateStartToken(sequenceToken!)) // 生成的流始终提供序列令牌。
            {
                Faults.onFirstMessageFault.TryFire(InjectFault);
                await WriteStateAsync();
            }

            State.LastProcessedToken = sequenceToken;
            if (evt.EventType != GeneratedEvent.GeneratedEventType.Report)
            {
                Faults.onFirstMessageProcessedFault.TryFire(InjectFault);
                Faults.on33rdMessageFault.TryFire(InjectFault);
                // 每处理 10 个事件，就为 Grain 状态建立检查点
                if (State.Accumulator % 10 != 0) return;
                logger.LogInformation(
                    "Checkpointing: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}.",
                    State.StreamGuid,
                    State.StreamNamespace,
                    sequenceToken,
                    State.Accumulator);
                await WriteStateAsync();
                return;
            }
            Faults.onLastMessageFault.TryFire(InjectFault);
            logger.LogInformation(
                "Final checkpointing: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}.",
                State.StreamGuid,
                State.StreamNamespace,
                sequenceToken,
                State.Accumulator);
            await WriteStateAsync();
            var reporter = GrainFactory.GetGrain<IGeneratedEventReporterGrain>(GeneratedStreamTestConstants.ReporterId);
            await reporter.ReportResult(this.GetPrimaryKey(), GeneratedStreamTestConstants.StreamProviderName, StreamNamespace, State.Accumulator);
        }

        private Task OnErrorAsync(Exception ex)
        {
            logger.LogInformation(ex, "Received an error on stream. StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}", State.StreamGuid, State.StreamNamespace);
            return Task.CompletedTask;
        }

        private void InjectFault() => InjectFault(activating: false);

        private void InjectFault(bool activating)
        {
            logger.LogInformation(
                "InjectingFault: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}.",
                State.StreamGuid,
                State.StreamNamespace,
                State.RecoveryToken,
                State.Accumulator);

            if (!activating)
            {
                DeactivateOnIdle(); // 终止 Grain 激活实例，并从检查点重新加载
            }

            throw new ApplicationException("Injecting Fault");
        }
    }
}
