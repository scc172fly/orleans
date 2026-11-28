using BenchmarkGrainInterfaces.MapReduce;

namespace BenchmarkGrains.MapReduce
{
    public class TargetGrain<TInput> : DataflowGrain, ITargetGrain<TInput>
    {
        private ITargetProcessor<TInput> _processor = null!;

        public Task Init(ITargetProcessor<TInput> processor)
        {
            this._processor = processor;
            return Task.CompletedTask;
        }

        public Task<GrainDataflowMessageStatus> OfferMessage(TInput messageValue, bool consumeToAccept)
        {
            throw new NotImplementedException();
        }

        public Task SendAsync(TInput t)
        {
            throw new NotImplementedException();
        }

#pragma warning disable CS0618 // 保留旧版令牌的基准测试实现。
        public Task SendAsync(TInput t, GrainCancellationToken gct)
#pragma warning restore CS0618
        {
            throw new NotImplementedException();
        }
    }

}
