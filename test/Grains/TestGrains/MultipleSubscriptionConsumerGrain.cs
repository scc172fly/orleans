using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Streams;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class MultipleSubscriptionConsumerGrain : Grain, IMultipleSubscriptionConsumerGrain
    {
        private readonly Dictionary<StreamSubscriptionHandle<int>, Tuple<Counter, Counter>> consumedMessageCounts;
        private readonly List<CountObserverRegistration> countObservers = [];
        private readonly ILogger logger;
        private int consumerCount = 0;

        public MultipleSubscriptionConsumerGrain(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger($"{this.GetType().Name}-{this.IdentityString}");
            consumedMessageCounts = new Dictionary<StreamSubscriptionHandle<int>, Tuple<Counter, Counter>>();
        }

        private class Counter
        {
            public int Value { get; private set; }

            public void Increment()
            {
                Value++;
            }

            public void Clear()
            {
                Value = 0;
            }
        }

        private sealed class CountObserverRegistration
        {
            public Guid HandleId { get; }
            public int ExpectedCount { get; }
            public IMultipleSubscriptionConsumerObserver Observer { get; }

            public CountObserverRegistration(Guid handleId, int expectedCount, IMultipleSubscriptionConsumerObserver observer)
            {
                HandleId = handleId;
                ExpectedCount = expectedCount;
                Observer = observer;
            }
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("OnActivateAsync");
            return Task.CompletedTask;
        }

        public async Task<StreamSubscriptionHandle<int>> BecomeConsumer(Guid streamId, string streamNamespace, string providerToUse)
        {
            logger.LogInformation("BecomeConsumer");

            // 为此订阅创建新计数器
            var count = new Counter();
            var error = new Counter();

            // 获取流
            IStreamProvider streamProvider = this.GetStreamProvider(providerToUse);
            var stream = streamProvider.GetStream<int>(streamNamespace, streamId);

            int countCapture = consumerCount;
            consumerCount++;
            // 订阅
            StreamSubscriptionHandle<int> handle = await stream.SubscribeAsync(
                (items) => OnNext(items, countCapture, count),
                e => OnError(e, countCapture, error));

            // 跟踪计数器
            consumedMessageCounts.Add(handle, Tuple.Create(count, error));
            NotifyObservers();

            // 返回句柄
            return handle;
        }

        public async Task<StreamSubscriptionHandle<int>> Resume(StreamSubscriptionHandle<int> handle)
        {
            logger.LogInformation("Resume");
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));

            // 为此订阅创建新计数器
            Tuple<Counter, Counter>? counters;
            if (!consumedMessageCounts.TryGetValue(handle, out counters))
            {
                counters = Tuple.Create(new Counter(), new Counter());
            }

            int countCapture = consumerCount;
            consumerCount++;
            // 订阅
            StreamSubscriptionHandle<int> newhandle = await handle.ResumeAsync(
                (items) => OnNext(items, countCapture, counters.Item1),
                e => OnError(e, countCapture, counters.Item2));

            // 跟踪计数器
            consumedMessageCounts[newhandle] = counters;
            NotifyObservers();

            // 返回句柄
            return newhandle;

        }

        public async Task StopConsuming(StreamSubscriptionHandle<int> handle)
        {
            logger.LogInformation("StopConsuming");
            // 取消订阅
            await handle.UnsubscribeAsync();

            // 停止跟踪此流的事件数量
            consumedMessageCounts.Remove(handle);
            countObservers.RemoveAll(observer => observer.HandleId == handle.HandleId);
        }

        public Task<IList<StreamSubscriptionHandle<int>>> GetAllSubscriptions(Guid streamId, string streamNamespace, string providerToUse)
        {
            logger.LogInformation("GetAllSubscriptionHandles");

            // 获取流
            IStreamProvider streamProvider = this.GetStreamProvider(providerToUse);
            var stream = streamProvider.GetStream<int>(streamNamespace, streamId);

            // 获取此流的全部活动订阅句柄。
            return stream.GetAllSubscriptionHandles();
        }

        public Task<Dictionary<StreamSubscriptionHandle<int>, Tuple<int, int>>> GetNumberConsumed()
        {
            logger.LogInformation(
                "ConsumedMessageCounts = {Counts}",
                Utils.EnumerableToString(
                    consumedMessageCounts,
                    kvp => $"Consumer: {kvp.Key.HandleId} -> count: {kvp.Value}"));

            return Task.FromResult(consumedMessageCounts.ToDictionary(kvp => kvp.Key, kvp => Tuple.Create(kvp.Value.Item1.Value, kvp.Value.Item2.Value)));
        }

        public Task ClearNumberConsumed()
        {
            logger.LogInformation("ClearNumberConsumed");
            foreach (var counters in consumedMessageCounts.Values)
            {
                counters.Item1.Clear();
                counters.Item2.Clear();
            }

            NotifyObservers();
            return Task.CompletedTask;
        }

        public Task Deactivate()
        {
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            logger.LogInformation("OnDeactivateAsync");
            return Task.CompletedTask;
        }

        public Task RegisterCountObserver(Guid handleId, int expectedCount, IMultipleSubscriptionConsumerObserver observer)
        {
            countObservers.Add(new CountObserverRegistration(handleId, expectedCount, observer));
            NotifyObservers();
            return Task.CompletedTask;
        }

        private Task OnNext(IList<SequentialItem<int>> items, int countCapture, Counter count)
        {
            foreach (SequentialItem<int> item in items)
            {
                logger.LogInformation("Got next event {Item} on handle {Handle}", item.Item, countCapture);
                var contextValue = RequestContext.Get(SampleStreaming_ProducerGrain.RequestContextKey) as string;
                if (!string.Equals(contextValue, SampleStreaming_ProducerGrain.RequestContextValue, StringComparison.Ordinal))
                {
                    throw new Exception($"Got the wrong RequestContext value {contextValue}.");
                }
                count.Increment();
            }

            NotifyObservers();
            return Task.CompletedTask;
        }

        private Task OnError(Exception e, int countCapture, Counter error)
        {
            logger.LogInformation(e, "Got exception on handle {Handle}", countCapture);
            error.Increment();
            NotifyObservers();
            return Task.CompletedTask;
        }

        private (int ConsumedCount, int ErrorCount)? GetCounts(Guid handleId)
        {
            foreach (var entry in consumedMessageCounts)
            {
                if (entry.Key.HandleId == handleId)
                {
                    return (entry.Value.Item1.Value, entry.Value.Item2.Value);
                }
            }

            return null;
        }

        private void NotifyObservers()
        {
            for (var i = countObservers.Count - 1; i >= 0; i--)
            {
                var observer = countObservers[i];
                var counts = GetCounts(observer.HandleId);
                if (counts is null)
                {
                    countObservers.RemoveAt(i);
                    continue;
                }

                var errorCount = counts.Value.ErrorCount;
                if (errorCount > 0)
                {
                    try
                    {
                        observer.Observer.ConsumptionFailed(observer.HandleId, errorCount);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Failed to notify observer of consumption failure for handle {HandleId}", observer.HandleId);
                    }

                    countObservers.RemoveAt(i);
                    continue;
                }

                var consumedCount = counts.Value.ConsumedCount;
                if (consumedCount >= observer.ExpectedCount)
                {
                    try
                    {
                        observer.Observer.ConsumedCountReached(observer.HandleId, consumedCount);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Failed to notify observer of consumed count reached for handle {HandleId}", observer.HandleId);
                    }

                    countObservers.RemoveAt(i);
                }
            }
        }
    }
}
