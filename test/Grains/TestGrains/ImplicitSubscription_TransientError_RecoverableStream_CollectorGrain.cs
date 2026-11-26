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

        // 失职注射
        // 模拟简单的瞬态故障 。
        // 每一次失败都发生一次。
        // 我们把失败放在关键点,以检验恢复能力。
        // - 一开始 grain 启动
        // - 激活后,但在存储开始标记之前。
        // - 开始后,标语已经存储, 但因为它的信息已经被处理。
        // - 中流处理(本次测试的第33条信息, 但真实取决于流)
        // - 最后一条信息在流中。
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

        // grain 实例状态
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
            stream = streamProvider.GetStream<GeneratedEvent>(State.StreamNamespace!, State.StreamGuid); // 命名空间在获取流之前先于上面初始化 。
            foreach (StreamSubscriptionHandle<GeneratedEvent> handle in await stream.GetAllSubscriptionHandles())
            {
                await handle.ResumeAsync(OnNextAsync, OnErrorAsync, State.RecoveryToken);
            }
        }

        private async Task OnNextAsync(GeneratedEvent evt, StreamSequenceToken? sequenceToken)
        {

            // 忽略重复
            if (State.IsDuplicate(sequenceToken!)) // 生成的流总是提供序列符号 。
            {
                logger.LogInformation("Received duplicate event.  StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);
                return;
            }

            logger.LogInformation("Received event.  StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);

            // 试图注射过失前加积蓄积器
            State.Accumulator++;

            // 我们只会更新启动代号, 如果这是我们处理的第一个事件
            // 如果那样的话,我们会想保存启动标志 以防出事
            if (State.TryUpdateStartToken(sequenceToken!)) // 生成的流总是提供序列符号 。
            {
                Faults.onFirstMessageFault.TryFire(InjectFault);
                await WriteStateAsync();
            }

            State.LastProcessedToken = sequenceToken;
            if (evt.EventType != GeneratedEvent.GeneratedEventType.Report)
            {
                Faults.onFirstMessageProcessedFault.TryFire(InjectFault);
                Faults.on33rdMessageFault.TryFire(InjectFault);
                // 每10次事件,检查我们的 grain 状态
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
                DeactivateOnIdle(); // 杀死 grain ,从检查站重新装载
            }

            throw new ApplicationException("Injecting Fault");
        }
    }
}
