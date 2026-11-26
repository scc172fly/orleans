using Orleans.Streams;

namespace Tester.StreamingTests
{
    //每个流服务商一个租赁经理 grain ,因此其关键是流服务商名称
    public interface ILeaseManagerGrain : IGrainWithStringKey
    {
        Task<QueueId> Acquire();
        Task<bool> Renew(QueueId leaseNumber);
        Task Release(QueueId leaseNumber);
        Task SetQueuesAsLeases(IEnumerable<QueueId> queues);
        //试验用的方法
        Task RecordBalancerResponsibility(string balancerId, int ownedQueues);
        Task<Dictionary<string, int>> GetResponsibilityMap(CancellationToken cancellationToken = default);

    }

    public class LeaseManagerGrain : Grain, ILeaseManagerGrain
    {
        //queueId 是这里的租赁代号
        private static readonly DateTime UnAssignedLeaseTime = DateTime.MinValue;
        private Dictionary<QueueId, DateTime> queueLeaseToRenewTimeMap = null!;
        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.queueLeaseToRenewTimeMap = new Dictionary<QueueId, DateTime>();
            this.responsibilityMap = new Dictionary<string, int>();
            return Task.CompletedTask;
        }

        public Task<QueueId> Acquire()
        {
            foreach (var lease in this.queueLeaseToRenewTimeMap)
            {
                //找到第一个未指定的租赁, 然后转让它
                if (lease.Value.Equals(UnAssignedLeaseTime))
                {
                    this.queueLeaseToRenewTimeMap[lease.Key] = DateTime.UtcNow;
                    return Task.FromResult(lease.Key);
                }
            }
            throw new KeyNotFoundException("No more lease to acquire");
        }

        public Task<bool> Renew(QueueId leaseNumber)
        {
            if (this.queueLeaseToRenewTimeMap.ContainsKey(leaseNumber))
            {
                this.queueLeaseToRenewTimeMap[leaseNumber] = DateTime.UtcNow;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task Release(QueueId leaseNumber)
        {
            if (this.queueLeaseToRenewTimeMap.ContainsKey(leaseNumber))
                this.queueLeaseToRenewTimeMap[leaseNumber] = UnAssignedLeaseTime;
            return Task.CompletedTask;
        }

        public Task SetQueuesAsLeases(IEnumerable<QueueId> queueIds)
        {
            //如果已经设置, 返回
            if (this.queueLeaseToRenewTimeMap.Count > 0)
                return Task.CompletedTask;
            //建立初始租赁地图
            foreach (var queueId in queueIds)
            {
                this.queueLeaseToRenewTimeMap.Add(queueId, UnAssignedLeaseTime);
            }
            return Task.CompletedTask;
        }

        //试验用的方法
        private Dictionary<string, int> responsibilityMap = null!;
        public Task RecordBalancerResponsibility(string balancerId, int ownedQueues)
        {
            responsibilityMap[balancerId] = ownedQueues;
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, int>> GetResponsibilityMap(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responsibilityMap);
        }
    }
}
