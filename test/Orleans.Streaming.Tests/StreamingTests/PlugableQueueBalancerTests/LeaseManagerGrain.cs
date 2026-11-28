using Orleans.Streams;

namespace Tester.StreamingTests
{
    //每个流提供程序对应一个租约管理 Grain，因此其键为流提供程序名称。
    public interface ILeaseManagerGrain : IGrainWithStringKey
    {
        Task<QueueId> Acquire();
        Task<bool> Renew(QueueId leaseNumber);
        Task Release(QueueId leaseNumber);
        Task SetQueuesAsLeases(IEnumerable<QueueId> queues);
        //测试断言使用的方法
        Task RecordBalancerResponsibility(string balancerId, int ownedQueues);
        Task<Dictionary<string, int>> GetResponsibilityMap(CancellationToken cancellationToken = default);

    }

    public class LeaseManagerGrain : Grain, ILeaseManagerGrain
    {
        //此处 queueId 即租约 ID。
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
                //查找第一个未分配的租约并将其分配出去。
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
            //如果已完成设置，则直接返回。
            if (this.queueLeaseToRenewTimeMap.Count > 0)
                return Task.CompletedTask;
            //初始化租约映射。
            foreach (var queueId in queueIds)
            {
                this.queueLeaseToRenewTimeMap.Add(queueId, UnAssignedLeaseTime);
            }
            return Task.CompletedTask;
        }

        //测试断言使用的方法
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
