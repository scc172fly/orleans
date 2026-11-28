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

            // 若调用执行后流中的最后一个值发生变化，则该流必定生成了一个值；
            // 为测试目的将其乘以二。
            if (this.lastStreamValue != initialLastStreamValue)
            {
                this.lastStreamValue *= 2;
            }
        }
    }
}
