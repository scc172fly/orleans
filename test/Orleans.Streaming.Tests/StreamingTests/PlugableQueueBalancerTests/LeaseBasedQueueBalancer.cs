using Orleans.Streams;

namespace Tester.StreamingTests
{
    //蠢蛋排队平衡器只租一次租约, 绝不续签, 仅作测试
    public class LeaseBasedQueueBalancerForTest : IStreamQueueBalancer
    {
        private readonly string id;
        private readonly ILeaseManagerGrain leaseManagerGrain;
        private readonly int _expectedResponsibility;
        private List<QueueId> ownedQueues = null!;

        public LeaseBasedQueueBalancerForTest(string name, int expectedResponsibility, IGrainFactory grainFactory)
        {
            this.leaseManagerGrain = grainFactory.GetGrain<ILeaseManagerGrain>(name);
            this.id = $"{name}-{Guid.NewGuid()}";
            _expectedResponsibility = expectedResponsibility;
        }

        public async Task Initialize(IStreamQueueMapper queueMapper)
        {
            await this.leaseManagerGrain.SetQueuesAsLeases(queueMapper.GetAllQueues());
            await GetInitialLease();
        }

        public Task Shutdown()
        {
            return Task.CompletedTask;
        }

        public IEnumerable<QueueId> GetMyQueues()
        {
            return this.ownedQueues;
        }

        private async Task GetInitialLease()
        {
            this.ownedQueues = new List<QueueId>(_expectedResponsibility);
            for (int i = 0; i < _expectedResponsibility; i++)
            {
                try
                {
                    this.ownedQueues.Add(await this.leaseManagerGrain.Acquire());
                }
                catch (KeyNotFoundException)
                { }
            }
            await this.leaseManagerGrain.RecordBalancerResponsibility(id.ToString(), this.ownedQueues.Count);
        }

        public bool SubscribeToQueueDistributionChangeEvents(IStreamQueueBalanceListener observer)
        {
            //无操作操作
            return true;
        }

        public bool UnSubscribeFromQueueDistributionChangeEvents(IStreamQueueBalanceListener observer)
        {
            //无操作操作
            return true;
        }
    }
}
