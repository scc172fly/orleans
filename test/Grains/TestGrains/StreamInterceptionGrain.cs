using Orleans.Streams;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    [ImplicitStreamSubscription("InterceptedStream")]
    public class StreamInterceptionGrain : Grain, IStreamInterceptionGrain, IIncomingGrainCallFilter
    {
        private int lastStreamValue;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var streams = this.GetStreamProvider("MemoryStreamProvider");
            var stream = streams.GetStream<int>("InterceptedStream", this.GetPrimaryKey());
            await stream.SubscribeAsync(
                (value, token) =>
                {
                    this.lastStreamValue = value;
                    return Task.CompletedTask;
                });
            await base.OnActivateAsync(cancellationToken);
        }

        public Task<int> GetLastStreamValue() => Task.FromResult(this.lastStreamValue);

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            var initialLastStreamValue = this.lastStreamValue;
            await context.Invoke();

            // 如果在引用后最后流流值发生变化,则流必须已经产生值, 翻倍
            // 用于测试目的。
            if (this.lastStreamValue != initialLastStreamValue)
            {
                this.lastStreamValue *= 2;
            }
        }
    }
}
