using Orleans.Streams;

namespace Tester.StreamingTests
{
    //仅用于测试的简易队列平衡器只获取一次租约，从不续租。
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
            //空操作
            return true;
        }

        public bool UnSubscribeFromQueueDistributionChangeEvents(IStreamQueueBalanceListener observer)
        {
            //空操作
            return true;
        }
    }
}
