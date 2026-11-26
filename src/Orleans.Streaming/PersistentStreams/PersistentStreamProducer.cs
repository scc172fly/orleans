using System;
using System.Threading.Tasks;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Orleans.Serialization;

namespace Orleans.Streams
{
    internal partial class PersistentStreamProducer<T> : IInternalAsyncBatchObserver<T>
    {
        private readonly StreamImpl<T> stream;
        private readonly IQueueAdapter queueAdapter;
        private readonly DeepCopier deepCopier;

        internal bool IsRewindable { get; private set; }

        internal PersistentStreamProducer(StreamImpl<T> stream, IStreamProviderRuntime providerUtilities, IQueueAdapter queueAdapter, bool isRewindable, DeepCopier deepCopier)
        {
            this.stream = stream;
            this.queueAdapter = queueAdapter;
            this.deepCopier = deepCopier;
            IsRewindable = isRewindable;
            var logger = providerUtilities.ServiceProvider.GetRequiredService<ILogger<PersistentStreamProducer<T>>>();
            LogCreatedPersistentStreamProducer(logger, stream, typeof(T), this.queueAdapter.Name);
        }

        public Task OnNextAsync(T item, StreamSequenceToken? token)
        {
            return this.queueAdapter.QueueMessageAsync(this.stream.StreamId, item, token, RequestContextExtensions.Export(this.deepCopier));
        }

        public Task OnNextBatchAsync(IEnumerable<T> batch, StreamSequenceToken? token)
        {
            return this.queueAdapter.QueueMessageBatchAsync(this.stream.StreamId, batch, token, RequestContextExtensions.Export(this.deepCopier));

        }

        public Task OnCompletedAsync()
        {
            // 也许可以给会合点发出一个密讯?
            throw new NotImplementedException("OnCompletedAsync is not implemented for now.");
        }

        public Task OnErrorAsync(Exception ex)
        {
            // 也许可以给会合点发出一个密讯?
            throw new NotImplementedException("OnErrorAsync is not implemented for now.");
        }

        public Task Cleanup()
        {
            return Task.CompletedTask;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Created PersistentStreamProducer for stream {StreamId}, of type {ElementType}, and with Adapter: {QueueAdapterName}."
        )]
        private static partial void LogCreatedPersistentStreamProducer(ILogger logger, StreamImpl<T> streamId, Type elementType, string queueAdapterName);
    }
}
