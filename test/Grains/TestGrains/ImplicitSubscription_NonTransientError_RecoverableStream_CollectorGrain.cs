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
    public class ImplicitSubscription_NonTransientError_RecoverableStream_CollectorGrain : Grain<StreamCheckpoint<int>>, IGeneratedEventCollectorGrain
    {
        public const string StreamNamespace = "NonTransientError_RecoverableStream";

        // Grain 实例状态
        private readonly ILogger logger;
        private IAsyncStream<GeneratedEvent> stream = null!;

        private class FaultsState
        {
            public bool FaultCleared { get; set; }
        }
        private static readonly ConcurrentDictionary<Guid, FaultsState> FaultInjectionTracker = new ConcurrentDictionary<Guid, FaultsState>();
        private FaultsState? myFaults;
        private FaultsState Faults { get { return myFaults ?? (myFaults = FaultInjectionTracker.GetOrAdd(this.GetPrimaryKey(), key => new FaultsState())); } }

        public ImplicitSubscription_NonTransientError_RecoverableStream_CollectorGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger("RecoverableStreamCollectorGrain " + base.IdentityString);
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");

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

            await stream.SubscribeAsync(OnNextAsync, OnErrorAsync, State.RecoveryToken!);
        }

        private async Task OnNextAsync(GeneratedEvent evt, StreamSequenceToken? sequenceToken)
        {
            // 忽略重复事件
            if (State.IsDuplicate(sequenceToken!)) // 生成的流始终提供序列令牌。
            {
                logger.LogInformation("Received duplicate event. StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);
                return;
            }

            logger.LogInformation("Received event. StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);

            // 仅当这是处理的第一个事件时，才更新起始令牌
            // 在这种情况下，需要保存起始令牌，以便发生故障时恢复。
            if (State.TryUpdateStartToken(sequenceToken!)) // 生成的流始终提供序列令牌。
            {
                await WriteStateAsync();
            }

            // 在第 33 个事件处触发故障，直到故障被清除
            if (State.Accumulator == 32 && !Faults.FaultCleared)
            {
                InjectFault();
            }

            State.Accumulator++;
            State.LastProcessedToken = sequenceToken;
            if (evt.EventType != GeneratedEvent.GeneratedEventType.Report)
            {
                // 每处理 10 个事件，就为 Grain 状态建立检查点
                if (State.Accumulator % 10 != 0) return;
                logger.LogInformation(
                    "Checkpointing: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}",
                    State.StreamGuid,
                    State.StreamNamespace,
                    sequenceToken,
                    State.Accumulator);
                await WriteStateAsync();
                return;
            }

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
            logger.LogInformation(
                ex,
                "Received an error on stream. StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}",
                State.StreamGuid,
                State.StreamNamespace);
            Faults.FaultCleared = true;
            return Task.CompletedTask;
        }

        private void InjectFault()
        {
            logger.LogInformation(
                "InjectingFault: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}.",
                State.StreamGuid,
                State.StreamNamespace,
                State.RecoveryToken,
                State.Accumulator);
            throw new ApplicationException("Injecting Fault");
        }
    }
}
