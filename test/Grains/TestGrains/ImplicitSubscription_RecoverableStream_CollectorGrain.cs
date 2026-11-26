using Microsoft.Extensions.Logging;
using Orleans.Providers;
using Orleans.Providers.Streams.Generator;
using Orleans.Streams;
using TestGrainInterfaces;
using UnitTests.Grains;

namespace TestGrains
{
    [ImplicitStreamSubscription(StreamNamespace)]
    [StorageProvider(ProviderName = StorageProviderName)]
    public class ImplicitSubscription_RecoverableStream_CollectorGrain : Grain<StreamCheckpoint<int>>, IGeneratedEventCollectorGrain
    {
        public const string StreamNamespace = "RecoverableStream";
        public const string StorageProviderName = "AzureStorage";

        // grain 实例状态
        private readonly ILogger logger;
        private IAsyncStream<GeneratedEvent> stream = null!;

        public ImplicitSubscription_RecoverableStream_CollectorGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
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
            stream = streamProvider.GetStream<GeneratedEvent>(State.StreamNamespace!, State.StreamGuid); // 命名空间在获取流之前先于上面初始化 。

            await stream.SubscribeAsync(OnNextAsync, OnErrorAsync, State.RecoveryToken!);
        }

        private async Task OnNextAsync(GeneratedEvent evt, StreamSequenceToken? sequenceToken)
        {

            // 忽略重复
            if (State.IsDuplicate(sequenceToken!)) // 生成的流总是提供序列符号 。
            {
                logger.LogInformation("Received duplicate event. StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);
                return;
            }

            logger.LogInformation("Received event. StreamGuid: {StreamGuid}, SequenceToken: {SequenceToken}", State.StreamGuid, sequenceToken);

            // 我们只会更新启动代号, 如果这是我们处理的第一个事件
            // 如果那样的话,我们会想保存启动标志 以防出事
            if (State.TryUpdateStartToken(sequenceToken!)) // 生成的流总是提供序列符号 。
            {
                await WriteStateAsync();
            }

            State.Accumulator++;
            State.LastProcessedToken = sequenceToken;
            if (evt.EventType != GeneratedEvent.GeneratedEventType.Report)
            {
                // 每10次事件,检查我们的 grain 状态
                if (State.Accumulator % 10 != 0) return;
                logger.LogInformation("Checkpointing: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}.", State.StreamGuid, State.StreamNamespace, sequenceToken, State.Accumulator);
                await WriteStateAsync();
                return;
            }
            logger.LogInformation("Final checkpointing: StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}, SequenceToken: {SequenceToken}, Accumulator: {Accumulator}.", State.StreamGuid, State.StreamNamespace, sequenceToken, State.Accumulator);
            await WriteStateAsync();
            var reporter = GrainFactory.GetGrain<IGeneratedEventReporterGrain>(GeneratedStreamTestConstants.ReporterId);
            await reporter.ReportResult(this.GetPrimaryKey(), GeneratedStreamTestConstants.StreamProviderName, StreamNamespace, State.Accumulator);
        }

        private Task OnErrorAsync(Exception ex)
        {
            logger.LogInformation(ex, "Received an error on stream. StreamGuid: {StreamGuid}, StreamNamespace: {StreamNamespace}", State.StreamGuid, State.StreamNamespace);
            return Task.CompletedTask;
        }
    }
}
